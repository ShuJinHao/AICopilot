import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useUiLayoutStore } from '@/stores/uiLayoutStore'

function createSessionStorageMock(initial: Record<string, string> = {}) {
  const state = new Map(Object.entries(initial))

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

describe('uiLayoutStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('falls back to plan when old trial tab is restored from session storage', () => {
    Object.defineProperty(globalThis, 'sessionStorage', {
      value: createSessionStorageMock({
        'aicopilot.ui.agentWorkbenchTab': 'trial'
      }),
      configurable: true
    })

    const store = useUiLayoutStore()

    expect(store.agentWorkbenchTab).toBe('plan')
  })
})
