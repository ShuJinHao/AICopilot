import { ref } from 'vue'
import { configService } from '@/services/configService'
import type { ArtifactWorkspaceSettings, ChatRuntimeSettings } from '@/types/app'

export function useAgentWorkspaceConfigDomain() {
  const runtimeSettings = ref<ChatRuntimeSettings | null>(null)
  const workspaceSettings = ref<ArtifactWorkspaceSettings | null>(null)

  async function refreshAgentWorkspaceSettings() {
    const [runtime, workspace] = await Promise.all([
      configService.getRuntimeSettings(),
      configService.getWorkspaceSettings()
    ])
    runtimeSettings.value = runtime
    workspaceSettings.value = workspace
  }

  return {
    runtimeSettings,
    workspaceSettings,
    refreshAgentWorkspaceSettings
  }
}
