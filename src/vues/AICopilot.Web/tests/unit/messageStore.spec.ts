import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useMessageStore } from '@/stores/messageStore'
import { useSessionStore } from '@/stores/sessionStore'

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
})
