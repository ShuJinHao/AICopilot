import { ref } from 'vue'
import { CONFIG_READ_PERMISSIONS } from '@/security/permissions'
import { configService } from '@/services/configService'
import { useAuthStore } from '@/stores/authStore'
import type { ConfigDomainStates } from '@/stores/config/configStoreTypes'
import type { SemanticSourceStatus } from '@/types/app'

export function useSemanticSourceConfigDomain(states: ConfigDomainStates) {
  const authStore = useAuthStore()
  const semanticSourceStatuses = ref<SemanticSourceStatus[]>([])

  async function refreshSemanticSourceStatuses() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.businessDatabase)) {
      semanticSourceStatuses.value = []
      return
    }

    states.loadingStates.semanticSource = true
    try {
      semanticSourceStatuses.value = await configService.getSemanticSourceStatuses()
    } finally {
      states.loadingStates.semanticSource = false
    }
  }

  return {
    semanticSourceStatuses,
    refreshSemanticSourceStatuses
  }
}
