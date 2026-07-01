import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useChatStore } from '@/stores/chatStore'
import { useSessionStore } from '@/stores/sessionStore'
import { ChunkType, MessageRole } from '@/types/protocols'

const chatServiceMock = vi.hoisted(() => ({
  sendMessageStream: vi.fn()
}))

vi.mock('@/services/chatService', () => ({
  chatService: chatServiceMock
}))

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

describe('chatStore run status integration', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.stubGlobal('sessionStorage', createSessionStorageMock())
    vi.clearAllMocks()
  })

  it('starts run status before stream chunks and completes it when chat stream closes', async () => {
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-1')
    const store = useChatStore()

    chatServiceMock.sendMessageStream.mockImplementation(async (_sessionId, _input, callbacks) => {
      const assistantMessage = store.currentMessages[1]!

      expect(assistantMessage.role).toBe(MessageRole.Assistant)
      expect(store.getRunStatusForMessage(assistantMessage)).toMatchObject({
        phase: 'understanding',
        summary: '正在理解问题'
      })

      callbacks.onChunkReceived({
        source: 'IntentRoutingExecutor',
        type: ChunkType.Intent,
        content: JSON.stringify([{ intent: 'Analysis.DeviceLog.Query', confidence: 0.92 }])
      })
      expect(store.getRunStatusForMessage(assistantMessage)).toMatchObject({
        phase: 'querying',
        summary: '正在准备 Cloud 只读查询'
      })

      callbacks.onChunkReceived({
        source: 'DataAnalysisExecutor',
        type: ChunkType.FunctionResult,
        content: JSON.stringify({
          id: 'call-1',
          name: 'queryDeviceLogs',
          result: JSON.stringify({ rowCount: 20 })
        })
      })
      expect(store.getRunStatusForMessage(assistantMessage)).toMatchObject({
        phase: 'querying',
        queryCount: 1,
        returnedRows: 20
      })

      callbacks.onChunkReceived({
        source: 'FinalAgentRunExecutor',
        type: ChunkType.Text,
        content: '最近 1 天有错误和警告日志。'
      })
      expect(store.getRunStatusForMessage(assistantMessage)).toMatchObject({
        phase: 'answering',
        summary: '正在生成回答'
      })

      callbacks.onComplete()
    })

    await store.sendMessage('查询最近 1 天日志')

    const assistantMessage = store.currentMessages[1]!
    expect(store.getRunStatusForMessage(assistantMessage)).toMatchObject({
      phase: 'completed',
      summary: '回答已完成',
      queryCount: 1,
      returnedRows: 20
    })
    expect(assistantMessage.isStreaming).toBe(false)
  })
})
