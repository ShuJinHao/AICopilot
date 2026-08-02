import { defineStore } from 'pinia'
import { reactive, ref } from 'vue'
import { CONFIG_STORE_MESSAGES } from '@/constants/messages'
import { useConversationTemplateConfigDomain } from '@/stores/config/conversationTemplateConfig'
import type {
  ConfigDomainStates,
  ConfigEditableDomain,
  ConfigLoadingDomain
} from '@/stores/config/configStoreTypes'
import { useLanguageModelConfigDomain } from '@/stores/config/languageModelConfig'
import { toStoreErrorMessage } from '@/stores/useDialogCrud'
import type { ConfigDialogMode } from '@/types/app'

export const useConfigStore = defineStore('config', () => {
  const isLoading = ref(false)
  const errorMessage = ref('')

  const loadingStates = reactive<Record<ConfigLoadingDomain, boolean>>({
    languageModel: false,
    conversationTemplate: false
  })

  const dialogStates = reactive<Record<ConfigEditableDomain, boolean>>({
    languageModel: false,
    conversationTemplate: false
  })

  const dialogModes = reactive<Record<ConfigEditableDomain, ConfigDialogMode>>({
    languageModel: 'create',
    conversationTemplate: 'create'
  })

  const submittingStates = reactive<Record<ConfigEditableDomain, boolean>>({
    languageModel: false,
    conversationTemplate: false
  })

  const actionErrors = reactive<Record<ConfigEditableDomain, string>>({
    languageModel: '',
    conversationTemplate: ''
  })

  const domainStates: ConfigDomainStates = {
    loadingStates,
    dialogStates,
    dialogModes,
    submittingStates,
    actionErrors
  }

  const languageModelDomain = useLanguageModelConfigDomain(domainStates)
  const conversationTemplateDomain = useConversationTemplateConfigDomain(domainStates)

  async function refresh() {
    isLoading.value = true
    errorMessage.value = ''

    try {
      await Promise.all([
        languageModelDomain.refreshLanguageModels(),
        conversationTemplateDomain.refreshConversationTemplates()
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
