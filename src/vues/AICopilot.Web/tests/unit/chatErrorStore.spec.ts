import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { resolveChatErrorMessage, useChatErrorStore } from '@/stores/chatErrorStore'

describe('chatErrorStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('prefers backend user-facing error text', () => {
    expect(
      resolveChatErrorMessage({
        code: 'approval_pending',
        userFacingMessage: 'custom approval message'
      })
    ).toBe('custom approval message')
  })

  it('scopes active errors to the current session', () => {
    const store = useChatErrorStore()

    store.bindCurrentSession('session-1')
    store.setSessionError('session-2', 'other session error')

    expect(store.errorMessage).toBe('')

    store.setSessionError('session-1', 'current session error')
    expect(store.errorMessage).toBe('current session error')

    store.clearSessionError('session-1')
    expect(store.errorMessage).toBe('')
  })
})
