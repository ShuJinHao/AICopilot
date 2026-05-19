import { ref } from 'vue'
import { CONFIG_STORE_MESSAGES } from '@/constants/messages'
import { CONFIG_READ_PERMISSIONS } from '@/security/permissions'
import { configService } from '@/services/configService'
import {
  createEmptyLanguageModelForm,
  toLanguageModelForm
} from '@/stores/configFormFactories'
import { useAuthStore } from '@/stores/authStore'
import { useDialogCrud } from '@/stores/useDialogCrud'
import type { ConfigDomainStates } from '@/stores/config/configStoreTypes'
import type { LanguageModelFormModel, LanguageModelSummary, LanguageModelUsage } from '@/types/app'

export function useLanguageModelConfigDomain(states: ConfigDomainStates) {
  const authStore = useAuthStore()
  const languageModels = ref<LanguageModelSummary[]>([])
  const currentLanguageModel = ref<LanguageModelFormModel>(createEmptyLanguageModelForm())

  async function refreshLanguageModels() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.languageModel)) {
      languageModels.value = []
      return
    }

    states.loadingStates.languageModel = true
    try {
      languageModels.value = await configService.getLanguageModels()
    } finally {
      states.loadingStates.languageModel = false
    }
  }

  const languageModelCrud = useDialogCrud({
    domain: 'languageModel',
    states,
    current: currentLanguageModel,
    messages: CONFIG_STORE_MESSAGES.languageModel,
    createEmptyForm: createEmptyLanguageModelForm,
    toForm: toLanguageModelForm,
    loadDetail: configService.getLanguageModel,
    saveForm: async (form, mode) => {
      const payload = {
        ...form,
        provider: form.provider.trim(),
        protocolType: form.protocolType.trim(),
        name: form.name.trim(),
        baseUrl: form.baseUrl.trim(),
        apiKey: form.apiKeyAction === 'replace' ? form.apiKey.trim() : '',
        clearApiKey: form.apiKeyAction === 'clear',
        maxTokens: form.contextWindowTokens,
        usages: form.usages.length > 0 ? form.usages : (['Chat'] as LanguageModelUsage[])
      }

      if (mode === 'create') {
        await configService.createLanguageModel(payload)
      } else {
        await configService.updateLanguageModel(payload)
      }
    },
    deleteItem: async (id) => {
      await configService.deleteLanguageModel(id)
    },
    afterSave: refreshLanguageModels,
    afterDelete: refreshLanguageModels
  })

  return {
    languageModels,
    currentLanguageModel,
    refreshLanguageModels,
    closeLanguageModelDialog: languageModelCrud.closeDialog,
    openCreateLanguageModelDialog: languageModelCrud.openCreateDialog,
    openEditLanguageModelDialog: languageModelCrud.openEditDialog,
    saveLanguageModel: languageModelCrud.saveDialog,
    deleteLanguageModel: languageModelCrud.deleteDialog
  }
}
