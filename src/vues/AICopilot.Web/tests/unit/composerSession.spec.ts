import { describe, expect, it } from 'vitest'
import { shouldResetComposerForSessionChange } from '@/utils/composerSession'

describe('composer resolved-session transitions', () => {
  it.each([
    { previous: null, next: null, expected: false, reason: 'the session list is still unresolved' },
    {
      previous: null,
      next: 'session-a',
      expected: false,
      reason: 'the initial session is hydrating',
    },
    {
      previous: 'session-a',
      next: 'session-a',
      expected: false,
      reason: 'the active session is refreshing',
    },
    {
      previous: 'session-a',
      next: 'session-b',
      expected: true,
      reason: 'the user switched sessions',
    },
    {
      previous: 'session-a',
      next: null,
      expected: false,
      reason: 'the transition has not committed',
    },
  ])('returns $expected when $reason', ({ previous, next, expected }) => {
    expect(shouldResetComposerForSessionChange(previous, next)).toBe(expected)
  })
})
