import { defineStore } from 'pinia'
import { reactive, ref } from 'vue'
import { CONFIG_STORE_MESSAGES } from '@/constants/messages'
import { useApprovalPolicyConfigDomain } from '@/stores/config/approvalPolicyConfig'
import { useBusinessDatabaseConfigDomain } from '@/stores/config/businessDatabaseConfig'
import { useConversationTemplateConfigDomain } from '@/stores/config/conversationTemplateConfig'
import type {
  ConfigDomainStates,
  ConfigEditableDomain,
  ConfigLoadingDomain,
  ReadOnlyConfigDomain
} from '@/stores/config/configStoreTypes'
import { useLanguageModelConfigDomain } from '@/stores/config/languageModelConfig'
import { useMcpServerConfigDomain } from '@/stores/config/mcpServerConfig'
import { useProviderReliabilityConfigDomain } from '@/stores/config/providerReliabilityConfig'
import { useSemanticSourceConfigDomain } from '@/stores/config/semanticSourceConfig'
import { toStoreErrorMessage } from '@/stores/useDialogCrud'
import type { ConfigDialogMode } from '@/types/app'

export const useConfigStore = defineStore('config', () => {
  const isLoading = ref(false)
  const errorMessage = ref('')

  const loadingStates = reactive<Record<ConfigLoadingDomain | ReadOnlyConfigDomain, boolean>>({
    languageModel: false,
    conversationTemplate: false,
    approvalPolicy: false,
    businessDatabase: false,
    mcpServer: false,
    semanticSource: false,
    providerReliability: false
  })

  const dialogStates = reactive<Record<ConfigEditableDomain, boolean>>({
    languageModel: false,
    conversationTemplate: false,
    approvalPolicy: false,
    businessDatabase: false,
    mcpServer: false
  })

  const dialogModes = reactive<Record<ConfigEditableDomain, ConfigDialogMode>>({
    languageModel: 'create',
    conversationTemplate: 'create',
    approvalPolicy: 'create',
    businessDatabase: 'create',
    mcpServer: 'create'
  })

  const submittingStates = reactive<Record<ConfigEditableDomain, boolean>>({
    languageModel: false,
    conversationTemplate: false,
    approvalPolicy: false,
    businessDatabase: false,
    mcpServer: false
  })

  const actionErrors = reactive<Record<ConfigEditableDomain, string>>({
    languageModel: '',
    conversationTemplate: '',
    approvalPolicy: '',
    businessDatabase: '',
    mcpServer: ''
  })

  const domainStates: ConfigDomainStates = {
    loadingStates,
    dialogStates,
    dialogModes,
    submittingStates,
    actionErrors
  }

  const languageModelDomain = useLanguageModelConfigDomain(domainStates)
  const providerReliabilityDomain = useProviderReliabilityConfigDomain(domainStates)
  const conversationTemplateDomain = useConversationTemplateConfigDomain(domainStates)
  const approvalPolicyDomain = useApprovalPolicyConfigDomain(domainStates)
  const semanticSourceDomain = useSemanticSourceConfigDomain(domainStates)
  const businessDatabaseDomain = useBusinessDatabaseConfigDomain(
    domainStates,
    semanticSourceDomain.refreshSemanticSourceStatuses
  )
  const mcpServerDomain = useMcpServerConfigDomain(domainStates)

  async function refresh() {
    isLoading.value = true
    errorMessage.value = ''

    try {
      await Promise.all([
        languageModelDomain.refreshLanguageModels(),
        providerReliabilityDomain.refreshProviderReliability(),
        conversationTemplateDomain.refreshConversationTemplates(),
        approvalPolicyDomain.refreshApprovalPolicies(),
        businessDatabaseDomain.refreshBusinessDatabases(),
        mcpServerDomain.refreshMcpServers(),
        semanticSourceDomain.refreshSemanticSourceStatuses()
      ])
    } catch (error) {
      errorMessage.value = toStoreErrorMessage(
        error,
        CONFIG_STORE_MESSAGES.pageLoadFailed,
        CONFIG_STORE_MESSAGES.pageLoadForbidden
      )
      throw error
    } finally {
      isLoading.value = false
    }
  }

  return {
    ...languageModelDomain,
    ...conversationTemplateDomain,
    ...approvalPolicyDomain,
    ...businessDatabaseDomain,
    ...mcpServerDomain,
    ...semanticSourceDomain,
    ...providerReliabilityDomain,
    isLoading,
    errorMessage,
    loadingStates,
    dialogStates,
    dialogModes,
    submittingStates,
    actionErrors,
    refresh
  }
})
