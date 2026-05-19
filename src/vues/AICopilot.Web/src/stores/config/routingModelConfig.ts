import { ref } from 'vue'
import { CONFIG_STORE_MESSAGES } from '@/constants/messages'
import { CONFIG_READ_PERMISSIONS } from '@/security/permissions'
import { configService } from '@/services/configService'
import {
  createEmptyRoutingModelForm,
  toRoutingModelForm
} from '@/stores/configFormFactories'
import { useAuthStore } from '@/stores/authStore'
import { useDialogCrud } from '@/stores/useDialogCrud'
import type { ConfigDomainStates } from '@/stores/config/configStoreTypes'
import type { RoutingModelFormModel, RoutingModelSummary } from '@/types/app'

export function useRoutingModelConfigDomain(
  states: ConfigDomainStates,
  refreshLanguageModels: () => Promise<void>
) {
  const authStore = useAuthStore()
  const routingModels = ref<RoutingModelSummary[]>([])
  const currentRoutingModel = ref<RoutingModelFormModel>(createEmptyRoutingModelForm())

  async function refreshRoutingModels() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.routingModel)) {
      routingModels.value = []
      return
    }

    states.loadingStates.routingModel = true
    try {
      routingModels.value = await configService.getRoutingModels()
    } finally {
      states.loadingStates.routingModel = false
    }
  }

  const routingModelCrud = useDialogCrud({
    domain: 'routingModel',
    states,
    current: currentRoutingModel,
    messages: CONFIG_STORE_MESSAGES.routingModel,
    createEmptyForm: createEmptyRoutingModelForm,
    toForm: toRoutingModelForm,
    loadDetail: configService.getRoutingModel,
    saveForm: async (form, mode) => {
      const payload = {
        ...form,
        name: form.name.trim()
      }

      if (mode === 'create') {
        await configService.createRoutingModel(payload)
      } else {
        await configService.updateRoutingModel(payload)
      }
    },
    deleteItem: async (id) => {
      await configService.deleteRoutingModel(id)
    },
    afterSave: async () => {
      await Promise.all([refreshRoutingModels(), refreshLanguageModels()])
    },
    afterDelete: refreshRoutingModels
  })

  async function activateRoutingModel(id: string) {
    states.submittingStates.routingModel = true
    states.actionErrors.routingModel = ''
    try {
      await configService.activateRoutingModel(id)
      await refreshRoutingModels()
    } finally {
      states.submittingStates.routingModel = false
    }
  }

  return {
    routingModels,
    currentRoutingModel,
    refreshRoutingModels,
    activateRoutingModel,
    closeRoutingModelDialog: routingModelCrud.closeDialog,
    openCreateRoutingModelDialog: routingModelCrud.openCreateDialog,
    openEditRoutingModelDialog: routingModelCrud.openEditDialog,
    saveRoutingModel: routingModelCrud.saveDialog,
    deleteRoutingModel: routingModelCrud.deleteDialog
  }
}
