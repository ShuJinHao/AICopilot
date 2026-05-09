import { ref } from 'vue'
import { CONFIG_STORE_MESSAGES } from '@/constants/messages'
import { CONFIG_READ_PERMISSIONS } from '@/security/permissions'
import { configService } from '@/services/configService'
import {
  createEmptyConversationTemplateForm,
  toConversationTemplateForm
} from '@/stores/configFormFactories'
import { useAuthStore } from '@/stores/authStore'
import { useDialogCrud } from '@/stores/useDialogCrud'
import type { ConfigDomainStates } from '@/stores/config/configStoreTypes'
import type { ConversationTemplateFormModel, ConversationTemplateSummary } from '@/types/app'

export function useConversationTemplateConfigDomain(states: ConfigDomainStates) {
  const authStore = useAuthStore()
  const conversationTemplates = ref<ConversationTemplateSummary[]>([])
  const currentConversationTemplate = ref<ConversationTemplateFormModel>(
    createEmptyConversationTemplateForm()
  )

  async function refreshConversationTemplates() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.conversationTemplate)) {
      conversationTemplates.value = []
      return
    }

    states.loadingStates.conversationTemplate = true
    try {
      conversationTemplates.value = await configService.getConversationTemplates()
    } finally {
      states.loadingStates.conversationTemplate = false
    }
  }

  const conversationTemplateCrud = useDialogCrud({
    domain: 'conversationTemplate',
    states,
    current: currentConversationTemplate,
    messages: CONFIG_STORE_MESSAGES.conversationTemplate,
    createEmptyForm: createEmptyConversationTemplateForm,
    toForm: toConversationTemplateForm,
    loadDetail: configService.getConversationTemplate,
    saveForm: async (form, mode) => {
      const payload = {
        ...form,
        name: form.name.trim(),
        description: form.description.trim(),
        systemPrompt: form.systemPrompt.trim()
      }

      if (mode === 'create') {
        await configService.createConversationTemplate(payload)
      } else {
        await configService.updateConversationTemplate(payload)
      }
    },
    deleteItem: async (id) => {
      await configService.deleteConversationTemplate(id)
    },
    afterSave: refreshConversationTemplates,
    afterDelete: refreshConversationTemplates
  })

  return {
    conversationTemplates,
    currentConversationTemplate,
    refreshConversationTemplates,
    closeConversationTemplateDialog: conversationTemplateCrud.closeDialog,
    openCreateConversationTemplateDialog: conversationTemplateCrud.openCreateDialog,
    openEditConversationTemplateDialog: conversationTemplateCrud.openEditDialog,
    saveConversationTemplate: conversationTemplateCrud.saveDialog,
    deleteConversationTemplate: conversationTemplateCrud.deleteDialog
  }
}
