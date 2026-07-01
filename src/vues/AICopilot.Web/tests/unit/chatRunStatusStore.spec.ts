import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useChatRunStatusStore, extractReturnedRowsFromFunctionResult } from '@/stores/chatRunStatusStore'
import { useSessionStore } from '@/stores/sessionStore'
import { ChunkType } from '@/types/protocols'

function createSessionStorageMock() {
  const state = new Map<string, string>()

  return {
    getItem(key: string) {
      return state.get(key) ?? null
    },
    setItem(key: string, value: string) {
      state.set(key, value)
    },
    removeItem(key: string) {
      state.delete(key)
    },
    clear() {
      state.clear()
    }
  }
}

describe('chatRunStatusStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.stubGlobal('sessionStorage', createSessionStorageMock())
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-07-01T00:00:00Z'))
  })

  afterEach(() => {
    useChatRunStatusStore().reset()
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('starts a run in understanding phase and advances elapsed time', () => {
    const sessionStore = useSessionStore()
    const store = useChatRunStatusStore()
    sessionStore.persistCurrentSession('session-1')

    store.startRun('session-1', 'message-1')
    vi.advanceTimersByTime(8000)

    expect(store.currentRunStatus).toMatchObject({
      sessionId: 'session-1',
      messageKey: 'message-1',
      phase: 'understanding',
      summary: '正在理解问题',
      elapsedMs: 8000
    })
  })

  it('maps function call chunks to querying phase', () => {
    const store = useChatRunStatusStore()
    store.startRun('session-1', 'message-1')

    store.advanceFromChunk('session-1', 'message-1', {
      source: 'DataAnalysisExecutor',
      type: ChunkType.FunctionCall,
      content: JSON.stringify({ id: 'call-1', name: 'queryDeviceLogs', args: '{}' })
    })

    expect(store.getStatus('session-1', 'message-1')).toMatchObject({
      phase: 'querying',
      summary: '正在查询 Cloud 只读数据'
    })
  })

  it('records query count and returned rows from real function result payloads', () => {
    const store = useChatRunStatusStore()
    store.startRun('session-1', 'message-1')

    store.advanceFromChunk('session-1', 'message-1', {
      source: 'DataAnalysisExecutor',
      type: ChunkType.FunctionResult,
      content: JSON.stringify({
        id: 'call-1',
        name: 'queryDeviceLogs',
        result: JSON.stringify({ rows: [{ level: 'ERROR' }, { level: 'WARN' }] })
      })
    })

    expect(store.getStatus('session-1', 'message-1')).toMatchObject({
      phase: 'querying',
      queryCount: 1,
      returnedRows: 2
    })
  })

  it('maps text chunks to answering phase', () => {
    const store = useChatRunStatusStore()
    store.startRun('session-1', 'message-1')

    store.advanceFromChunk('session-1', 'message-1', {
      source: 'FinalAgentRunExecutor',
      type: ChunkType.Text,
      content: '分析结论'
    })

    expect(store.getStatus('session-1', 'message-1')).toMatchObject({
      phase: 'answering',
      summary: '正在生成回答'
    })
  })

  it('marks stream completion without overriding failed runs', () => {
    const store = useChatRunStatusStore()
    store.startRun('session-1', 'message-1')
    store.completeRun('session-1', 'message-1')

    expect(store.getStatus('session-1', 'message-1')).toMatchObject({
      phase: 'completed',
      summary: '回答已完成'
    })

    store.startRun('session-1', 'message-2')
    store.advanceFromChunk('session-1', 'message-2', {
      source: 'ChatStreamHandler',
      type: ChunkType.Error,
      content: JSON.stringify({
        code: 'data_analysis_failed',
        userFacingMessage: 'DataAnalysis 查询失败'
      })
    })
    store.completeRun('session-1', 'message-2')

    expect(store.getStatus('session-1', 'message-2')).toMatchObject({
      phase: 'failed',
      summary: 'DataAnalysis 查询失败',
      error: {
        code: 'data_analysis_failed',
        message: 'DataAnalysis 查询失败'
      }
    })
  })

  it('keeps run status scoped by session and message', () => {
    const sessionStore = useSessionStore()
    const store = useChatRunStatusStore()

    sessionStore.persistCurrentSession('session-a')
    store.startRun('session-a', 'message-a')

    sessionStore.persistCurrentSession('session-b')
    expect(store.currentRunStatus).toBeNull()

    store.startRun('session-b', 'message-b')

    expect(store.currentRunStatus?.messageKey).toBe('message-b')
    expect(store.getStatus('session-a', 'message-a')?.phase).toBe('understanding')
    expect(store.getStatus('session-a', 'message-b')).toBeNull()

    store.clearSession('session-b')

    expect(store.currentRunStatus).toBeNull()
    expect(store.getStatus('session-a', 'message-a')?.phase).toBe('understanding')
  })

  it('extracts row count only from structured function result facts', () => {
    expect(extractReturnedRowsFromFunctionResult({
      source: 'DataAnalysisExecutor',
      type: ChunkType.FunctionResult,
      content: JSON.stringify({ result: JSON.stringify({ rowCount: 20 }) })
    })).toBe(20)

    expect(extractReturnedRowsFromFunctionResult({
      source: 'DataAnalysisExecutor',
      type: ChunkType.FunctionResult,
      content: JSON.stringify({ result: JSON.stringify({ rows: [{ id: 1 }, { id: 2 }, { id: 3 }] }) })
    })).toBe(3)

    expect(extractReturnedRowsFromFunctionResult({
      source: 'DataAnalysisExecutor',
      type: ChunkType.FunctionResult,
      content: JSON.stringify({ result: '没有结构化行数' })
    })).toBeUndefined()
  })
})
