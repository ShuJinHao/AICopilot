import { describe, expect, it, vi } from 'vitest'
import {
  clearStaleChunkReloadFlag,
  isStaleChunkLoadError,
  recoverFromStaleChunkLoad
} from '@/router/chunkReloadGuard'

function createRuntime() {
  const values = new Map<string, string>()

  const sessionStorage = {
    getItem: vi.fn((key: string) => values.get(key) ?? null),
    setItem: vi.fn((key: string, value: string) => values.set(key, value)),
    removeItem: vi.fn((key: string) => values.delete(key))
  }

  return {
    reload: vi.fn(),
    sessionStorage
  }
}

describe('chunkReloadGuard', () => {
  it('identifies dynamic import and stale chunk failures', () => {
    expect(
      isStaleChunkLoadError(
        new TypeError('Failed to fetch dynamically imported module: /assets/ChatView-old.js')
      )
    ).toBe(true)
    expect(
      isStaleChunkLoadError(
        'Expected a JavaScript module script but the server responded with a MIME type of "text/html"'
      )
    ).toBe(true)
    expect(isStaleChunkLoadError(new Error('ordinary navigation failure'))).toBe(false)
  })

  it('reloads once for a stale chunk failure', () => {
    const runtime = createRuntime()

    expect(
      recoverFromStaleChunkLoad(
        new TypeError('Failed to fetch dynamically imported module: /assets/ChatView-old.js'),
        runtime
      )
    ).toBe(true)
    expect(runtime.reload).toHaveBeenCalledTimes(1)

    expect(
      recoverFromStaleChunkLoad(
        new TypeError('Failed to fetch dynamically imported module: /assets/ChatView-old.js'),
        runtime
      )
    ).toBe(false)
    expect(runtime.reload).toHaveBeenCalledTimes(1)
  })

  it('clears the reload marker after a successful navigation', () => {
    const runtime = createRuntime()

    recoverFromStaleChunkLoad(
      new TypeError('Failed to fetch dynamically imported module: /assets/ChatView-old.js'),
      runtime
    )
    clearStaleChunkReloadFlag(runtime)

    expect(
      recoverFromStaleChunkLoad(
        new TypeError('Failed to fetch dynamically imported module: /assets/ChatView-old.js'),
        runtime
      )
    ).toBe(true)
    expect(runtime.reload).toHaveBeenCalledTimes(2)
  })
})
