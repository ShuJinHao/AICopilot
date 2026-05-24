import { ref } from 'vue'
import { defineStore } from 'pinia'

export type AgentWorkbenchTab = 'plan' | 'steps' | 'approvals' | 'artifacts' | 'audit' | 'trial' | 'boundary'
export type VisualDensity = 'comfortable' | 'compact'

const STORAGE_KEY = 'aicopilot.ui.agentWorkbenchTab'

function readInitialTab(): AgentWorkbenchTab {
  const saved = sessionStorage.getItem(STORAGE_KEY)
  if (
    saved === 'plan' ||
    saved === 'steps' ||
    saved === 'approvals' ||
    saved === 'artifacts' ||
    saved === 'audit' ||
    saved === 'trial' ||
    saved === 'boundary'
  ) {
    return saved
  }

  return 'plan'
}

export const useUiLayoutStore = defineStore('uiLayout', () => {
  const isIconDockCollapsed = ref(false)
  const isSessionRailCollapsed = ref(false)
  const isAgentWorkbenchDrawerOpen = ref(false)
  const agentWorkbenchTab = ref<AgentWorkbenchTab>(readInitialTab())
  const isAgentWorkbenchTabPinned = ref(false)
  const agentWorkbenchWidth = ref(360)
  const visualDensity = ref<VisualDensity>('comfortable')

  function setAgentWorkbenchTab(tab: AgentWorkbenchTab, manual = true) {
    agentWorkbenchTab.value = tab
    isAgentWorkbenchTabPinned.value = manual
    sessionStorage.setItem(STORAGE_KEY, tab)
  }

  function suggestAgentWorkbenchTab(tab: AgentWorkbenchTab) {
    if (!isAgentWorkbenchTabPinned.value) {
      setAgentWorkbenchTab(tab, false)
    }
  }

  function unpinAgentWorkbenchTab() {
    isAgentWorkbenchTabPinned.value = false
  }

  function toggleSessionRail() {
    isSessionRailCollapsed.value = !isSessionRailCollapsed.value
  }

  function setAgentWorkbenchDrawerOpen(open: boolean) {
    isAgentWorkbenchDrawerOpen.value = open
  }

  function setAgentWorkbenchWidth(width: number) {
    agentWorkbenchWidth.value = Math.min(420, Math.max(320, width))
  }

  function setVisualDensity(density: VisualDensity) {
    visualDensity.value = density
  }

  return {
    isIconDockCollapsed,
    isSessionRailCollapsed,
    isAgentWorkbenchDrawerOpen,
    agentWorkbenchTab,
    isAgentWorkbenchTabPinned,
    agentWorkbenchWidth,
    visualDensity,
    setAgentWorkbenchTab,
    suggestAgentWorkbenchTab,
    unpinAgentWorkbenchTab,
    toggleSessionRail,
    setAgentWorkbenchDrawerOpen,
    setAgentWorkbenchWidth,
    setVisualDensity
  }
})
