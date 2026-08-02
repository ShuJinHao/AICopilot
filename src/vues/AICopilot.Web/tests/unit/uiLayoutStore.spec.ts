import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useUiLayoutStore } from '@/stores/uiLayoutStore'

describe('uiLayoutStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('keeps only current chat layout state', () => {
    const store = useUiLayoutStore()

    expect(store.isSessionRailCollapsed).toBe(false)
    store.toggleSessionRail()
    expect(store.isSessionRailCollapsed).toBe(true)
    expect('agentWorkbenchTab' in store).toBe(false)
  })
})
