import { ref } from 'vue'
import { configService } from '@/services/configService'
import type { ArtifactWorkspaceSettings, ChatRuntimeSettings } from '@/types/app'
import type { AgentRunQueuePage, AgentRunQueueSummary, AgentWorkerStatus } from '@/types/protocols'

export function useAgentWorkspaceConfigDomain() {
  const runtimeSettings = ref<ChatRuntimeSettings | null>(null)
  const workspaceSettings = ref<ArtifactWorkspaceSettings | null>(null)
  const runQueueSummary = ref<AgentRunQueueSummary | null>(null)
  const runQueuePage = ref<AgentRunQueuePage | null>(null)
  const workerStatus = ref<AgentWorkerStatus | null>(null)

  async function refreshAgentWorkspaceSettings() {
    const [runtime, workspace, queueSummary, queuePage, workers] = await Promise.all([
      configService.getRuntimeSettings(),
      configService.getWorkspaceSettings(),
      configService.getAgentRunQueueSummary(),
      configService.getAgentRunQueue(),
      configService.getAgentWorkerStatus()
    ])
    runtimeSettings.value = runtime
    workspaceSettings.value = workspace
    runQueueSummary.value = queueSummary
    runQueuePage.value = queuePage
    workerStatus.value = workers
  }

  return {
    runtimeSettings,
    workspaceSettings,
    runQueueSummary,
    runQueuePage,
    workerStatus,
    refreshAgentWorkspaceSettings
  }
}
