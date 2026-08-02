import { describe, expect, it } from 'vitest'
import { createSessionScopedState, resetSessionScopedState } from '@/stores/sessionScopedState'

describe('sessionScopedState', () => {
  it('resets the per-session chat run projection through one entry point', () => {
    const state = createSessionScopedState()
    state.chatRunStatus = {
      sessionId: 'session-1',
      messageKey: 'message-1',
      phase: 'querying',
      startedAt: '2026-07-01T00:00:00Z',
      elapsedMs: 1200,
      summary: '正在查询 Cloud 只读数据',
    }

    resetSessionScopedState(state)

    expect(state).toEqual(createSessionScopedState())
  })
})
