import { ref } from 'vue'
import { CONFIG_READ_PERMISSIONS } from '@/security/permissions'
import { configService } from '@/services/configService'
import { useAuthStore } from '@/stores/authStore'
import type { ConfigDomainStates } from '@/stores/config/configStoreTypes'
import type {
  CloudReadonlyReadiness,
  CloudReadonlySandboxAgentTrialStatus,
  CloudReadonlySandboxControlledTrialStatus,
  CloudReadonlySandboxStatus,
  ToolCatalogSummary,
  ToolRegistrySummary
} from '@/types/app'

export function useToolRegistryConfigDomain(states: ConfigDomainStates) {
  const authStore = useAuthStore()
  const toolRegistrations = ref<ToolRegistrySummary[]>([])
  const toolCatalog = ref<ToolCatalogSummary | null>(null)
  const cloudReadonlyReadiness = ref<CloudReadonlyReadiness | null>(null)
  const cloudReadonlyReadinessHistory = ref<CloudReadonlyReadiness[]>([])
  const cloudReadonlySandboxStatus = ref<CloudReadonlySandboxStatus | null>(null)
  const cloudReadonlySandboxSmokeHistory = ref<CloudReadonlySandboxStatus[]>([])
  const cloudReadonlySandboxAgentTrialStatus = ref<CloudReadonlySandboxAgentTrialStatus | null>(null)
  const cloudReadonlySandboxControlledTrialStatus =
    ref<CloudReadonlySandboxControlledTrialStatus | null>(null)

  async function refreshToolRegistry() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.toolRegistry)) {
      toolRegistrations.value = []
      toolCatalog.value = null
      cloudReadonlyReadiness.value = null
      cloudReadonlyReadinessHistory.value = []
      cloudReadonlySandboxStatus.value = null
      cloudReadonlySandboxSmokeHistory.value = []
      cloudReadonlySandboxAgentTrialStatus.value = null
      cloudReadonlySandboxControlledTrialStatus.value = null
      return
    }

    states.loadingStates.toolRegistry = true
    states.loadingStates.cloudReadonlyReadiness = true
    try {
      const [
        registrations,
        catalog,
        readiness,
        readinessHistory,
        sandboxStatus,
        sandboxHistory,
        sandboxAgentTrialStatus,
        sandboxControlledTrialStatus
      ] =
        await Promise.all([
          configService.getToolRegistrations(),
          configService.getToolCatalog(),
          configService.getCloudReadonlyReadiness(),
          configService.getCloudReadonlyReadinessHistory(),
          configService.getCloudReadonlySandboxStatus(),
          configService.getCloudReadonlySandboxSmokeHistory(),
          configService.getCloudReadonlySandboxAgentTrialStatus(),
          configService.getCloudReadonlySandboxControlledTrialStatus()
        ])
      toolRegistrations.value = registrations
      toolCatalog.value = catalog
      cloudReadonlyReadiness.value = readiness
      cloudReadonlyReadinessHistory.value = readinessHistory
      cloudReadonlySandboxStatus.value = sandboxStatus
      cloudReadonlySandboxSmokeHistory.value = sandboxHistory
      cloudReadonlySandboxAgentTrialStatus.value = sandboxAgentTrialStatus
      cloudReadonlySandboxControlledTrialStatus.value = sandboxControlledTrialStatus
    } finally {
      states.loadingStates.toolRegistry = false
      states.loadingStates.cloudReadonlyReadiness = false
    }
  }

  async function runCloudReadonlyReadinessCheck() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.cloudReadonlyReadiness)) return
    states.loadingStates.cloudReadonlyReadiness = true
    try {
      cloudReadonlyReadiness.value = await configService.runCloudReadonlyReadinessCheck('FakeEndpoint')
      cloudReadonlyReadinessHistory.value = await configService.getCloudReadonlyReadinessHistory()
      cloudReadonlySandboxStatus.value = await configService.getCloudReadonlySandboxStatus()
      cloudReadonlySandboxAgentTrialStatus.value =
        await configService.getCloudReadonlySandboxAgentTrialStatus()
      cloudReadonlySandboxControlledTrialStatus.value =
        await configService.getCloudReadonlySandboxControlledTrialStatus()
    } finally {
      states.loadingStates.cloudReadonlyReadiness = false
    }
  }

  async function runCloudReadonlySandboxSmoke() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.cloudReadonlyReadiness)) return
    states.loadingStates.cloudReadonlyReadiness = true
    try {
      cloudReadonlyReadiness.value = await configService.runCloudReadonlyReadinessCheck('RealSandboxSmoke')
      cloudReadonlyReadinessHistory.value = await configService.getCloudReadonlyReadinessHistory()
      cloudReadonlySandboxStatus.value = await configService.getCloudReadonlySandboxStatus()
      cloudReadonlySandboxSmokeHistory.value = await configService.getCloudReadonlySandboxSmokeHistory()
      cloudReadonlySandboxAgentTrialStatus.value =
        await configService.getCloudReadonlySandboxAgentTrialStatus()
      cloudReadonlySandboxControlledTrialStatus.value =
        await configService.getCloudReadonlySandboxControlledTrialStatus()
    } finally {
      states.loadingStates.cloudReadonlyReadiness = false
    }
  }

  return {
    toolRegistrations,
    toolCatalog,
    cloudReadonlyReadiness,
    cloudReadonlyReadinessHistory,
    cloudReadonlySandboxStatus,
    cloudReadonlySandboxSmokeHistory,
    cloudReadonlySandboxAgentTrialStatus,
    cloudReadonlySandboxControlledTrialStatus,
    refreshToolRegistry,
    runCloudReadonlyReadinessCheck,
    runCloudReadonlySandboxSmoke
  }
}
