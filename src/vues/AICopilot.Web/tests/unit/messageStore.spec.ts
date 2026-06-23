import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useMessageStore } from '@/stores/messageStore'
import { useSessionStore } from '@/stores/sessionStore'
import { ChunkType } from '@/types/protocols'
import type { WidgetChunk } from '@/types/models'

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

describe('messageStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.stubGlobal('sessionStorage', createSessionStorageMock())
  })

  it('restores persisted model snapshot data from chat history', () => {
    const sessionStore = useSessionStore()
    const messageStore = useMessageStore()
    sessionStore.persistCurrentSession('session-1')

    messageStore.setHistory('session-1', [
      {
        sessionId: 'session-1',
        role: 'Assistant',
        content: 'history reply',
        createdAt: '2026-05-13T09:00:00Z',
        finalModelId: 'final-1',
        finalModelName: 'deepseek-v4-pro',
        routingModelId: 'routing-1',
        routingModelName: 'deepseek-v4-flash',
        contextWindowTokens: 1000000,
        maxOutputTokens: 4096
      }
    ])

    expect(messageStore.currentMessages).toHaveLength(1)
    expect(messageStore.currentMessages[0]?.finalModelId).toBe('final-1')
    expect(messageStore.currentMessages[0]?.finalModelName).toBe('deepseek-v4-pro')
    expect(messageStore.currentMessages[0]?.routingModelId).toBe('routing-1')
    expect(messageStore.currentMessages[0]?.routingModelName).toBe('deepseek-v4-flash')
    expect(messageStore.currentMessages[0]?.contextWindowTokens).toBe(1000000)
    expect(messageStore.currentMessages[0]?.maxOutputTokens).toBe(4096)
  })

  it('marks assistant history without snapshot metadata as unknown model', () => {
    const sessionStore = useSessionStore()
    const messageStore = useMessageStore()
    sessionStore.persistCurrentSession('session-1')

    messageStore.setHistory('session-1', [
      {
        sessionId: 'session-1',
        role: 'Assistant',
        content: 'legacy reply',
        createdAt: '2026-05-13T09:00:00Z'
      }
    ])

    expect(messageStore.currentMessages[0]?.finalModelName).toBe('\u672a\u77e5')
    expect(messageStore.currentMessages[0]?.routingModelName).toBeNull()
  })

  it('restores stable render chunks from chat history without replaying runtime details', () => {
    const sessionStore = useSessionStore()
    const messageStore = useMessageStore()
    sessionStore.persistCurrentSession('session-1')

    messageStore.setHistory('session-1', [
      {
        messageId: 42,
        sequence: 2,
        sessionId: 'session-1',
        role: 'Assistant',
        content: 'structured reply',
        createdAt: '2026-05-13T09:00:00Z',
        renderChunks: [
          {
            source: 'IntentRoutingExecutor',
            type: ChunkType.Intent,
            content: JSON.stringify([{ intent: 'Analysis.Device', confidence: 0.91 }])
          },
          {
            source: 'DataAnalysisExecutor',
            type: ChunkType.FunctionCall,
            content: JSON.stringify({ id: 'call-1', name: 'queryDeviceLogs', args: '{}' })
          },
          {
            source: 'DataAnalysisExecutor',
            type: ChunkType.FunctionResult,
            content: JSON.stringify({ id: 'call-1', result: '[1]' })
          },
          {
            source: 'DataAnalysisExecutor',
            type: ChunkType.Widget,
            content: JSON.stringify({ id: 'w1', type: 'StatsCard', title: '异常数', description: '', data: { value: 3 } })
          },
          {
            source: 'FinalAgentRunExecutor',
            type: ChunkType.ApprovalRequest,
            content: JSON.stringify({
              callId: 'approval-1',
              name: 'generate_pdf',
              args: {},
              requiresOnsiteAttestation: false
            })
          }
        ]
      }
    ])

    const restored = messageStore.currentMessages[0]!
    expect(restored.messageId).toBe(42)
    expect(restored.sequence).toBe(2)
    expect(restored.chunks.some((chunk) => chunk.type === ChunkType.Intent)).toBe(false)
    expect(restored.chunks.some((chunk) => chunk.type === ChunkType.FunctionCall)).toBe(false)
    expect(restored.chunks.some((chunk) => chunk.type === ChunkType.FunctionResult)).toBe(false)
    expect(restored.chunks.some((chunk) => chunk.type === ChunkType.ApprovalRequest)).toBe(false)
    expect((restored.chunks.find((chunk) => chunk.type === ChunkType.Widget) as WidgetChunk).widget.title).toBe('异常数')
  })

  it('prepends older history without duplicating existing messages', () => {
    const sessionStore = useSessionStore()
    const messageStore = useMessageStore()
    sessionStore.persistCurrentSession('session-1')

    messageStore.setHistory('session-1', [
      {
        messageId: 2,
        sequence: 2,
        sessionId: 'session-1',
        role: 'Assistant',
        content: 'newer',
        createdAt: '2026-05-13T09:01:00Z'
      }
    ])

    messageStore.prependHistory('session-1', [
      {
        messageId: 1,
        sequence: 1,
        sessionId: 'session-1',
        role: 'User',
        content: 'older',
        createdAt: '2026-05-13T09:00:00Z'
      },
      {
        messageId: 2,
        sequence: 2,
        sessionId: 'session-1',
        role: 'Assistant',
        content: 'newer duplicate',
        createdAt: '2026-05-13T09:01:00Z'
      }
    ])

    expect(messageStore.currentMessages.map((message) => message.messageId)).toEqual([1, 2])
    expect(messageStore.currentMessages.map((message) => message.chunks[0]?.content)).toEqual([
      'older',
      'newer'
    ])
  })
})
