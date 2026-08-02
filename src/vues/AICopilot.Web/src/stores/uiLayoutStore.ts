import { ref } from 'vue'
import { defineStore } from 'pinia'

export type VisualDensity = 'comfortable' | 'compact'

export const useUiLayoutStore = defineStore('uiLayout', () => {
  const isIconDockCollapsed = ref(false)
  const isSessionRailCollapsed = ref(false)
  const visualDensity = ref<VisualDensity>('comfortable')

  function toggleSessionRail() {
    isSessionRailCollapsed.value = !isSessionRailCollapsed.value
  }

  function setVisualDensity(density: VisualDensity) {
    visualDensity.value = density
  }

  return {
    isIconDockCollapsed,
    isSessionRailCollapsed,
    visualDensity,
    toggleSessionRail,
    setVisualDensity
  }
})
