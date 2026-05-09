import { ref } from 'vue'
import { CONFIG_READ_PERMISSIONS } from '@/security/permissions'
import { configService } from '@/services/configService'
import { useAuthStore } from '@/stores/authStore'
import type { ConfigDomainStates } from '@/stores/config/configStoreTypes'
import type { ProviderReliabilityConfig } from '@/types/app'

export function useProviderReliabilityConfigDomain(states: ConfigDomainStates) {
  const authStore = useAuthStore()
  const providerReliability = ref<ProviderReliabilityConfig | null>(null)

  async function refreshProviderReliability() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.providerReliability)) {
      providerReliability.value = null
      return
    }

    states.loadingStates.providerReliability = true
    try {
      providerReliability.value = await configService.getProviderReliability()
    } finally {
      states.loadingStates.providerReliability = false
    }
  }

  return {
    providerReliability,
    refreshProviderReliability
  }
}
