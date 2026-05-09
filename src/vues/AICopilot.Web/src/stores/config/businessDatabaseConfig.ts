import { ref } from 'vue'
import { FORM_DEFAULTS } from '@/constants/formDefaults'
import { CONFIG_STORE_MESSAGES } from '@/constants/messages'
import { CONFIG_READ_PERMISSIONS } from '@/security/permissions'
import { configService } from '@/services/configService'
import {
  createEmptyBusinessDatabaseForm,
  toBusinessDatabaseForm
} from '@/stores/configFormFactories'
import { useAuthStore } from '@/stores/authStore'
import { useDialogCrud } from '@/stores/useDialogCrud'
import type { ConfigDomainStates } from '@/stores/config/configStoreTypes'
import type { BusinessDatabaseFormModel, BusinessDatabaseSummary } from '@/types/app'

export function useBusinessDatabaseConfigDomain(
  states: ConfigDomainStates,
  refreshSemanticSourceStatuses: () => Promise<void>
) {
  const authStore = useAuthStore()
  const businessDatabases = ref<BusinessDatabaseSummary[]>([])
  const currentBusinessDatabase = ref<BusinessDatabaseFormModel>(createEmptyBusinessDatabaseForm())
  const currentBusinessDatabaseProviderSnapshot = ref<number>(
    FORM_DEFAULTS.businessDatabase.provider
  )

  async function refreshBusinessDatabases() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.businessDatabase)) {
      businessDatabases.value = []
      return
    }

    states.loadingStates.businessDatabase = true
    try {
      businessDatabases.value = await configService.getBusinessDatabases()
    } finally {
      states.loadingStates.businessDatabase = false
    }
  }

  const businessDatabaseCrud = useDialogCrud({
    domain: 'businessDatabase',
    states,
    current: currentBusinessDatabase,
    messages: CONFIG_STORE_MESSAGES.businessDatabase,
    createEmptyForm: createEmptyBusinessDatabaseForm,
    toForm: toBusinessDatabaseForm,
    loadDetail: configService.getBusinessDatabase,
    saveForm: async (form, mode) => {
      const connectionString = form.connectionString.trim()
      const provider =
        mode === 'edit' && connectionString.length === 0
          ? currentBusinessDatabaseProviderSnapshot.value
          : form.provider
      const payload = {
        ...form,
        name: form.name.trim(),
        description: form.description.trim(),
        connectionString,
        provider,
        isReadOnly: true,
        externalSystemType: form.externalSystemType,
        readOnlyCredentialVerified: form.readOnlyCredentialVerified
      }

      if (mode === 'create') {
        await configService.createBusinessDatabase(payload)
      } else {
        await configService.updateBusinessDatabase(payload)
      }
    },
    deleteItem: async (id) => {
      await configService.deleteBusinessDatabase(id)
    },
    afterClose: () => {
      currentBusinessDatabaseProviderSnapshot.value = FORM_DEFAULTS.businessDatabase.provider
    },
    afterOpenCreate: () => {
      currentBusinessDatabaseProviderSnapshot.value = FORM_DEFAULTS.businessDatabase.provider
    },
    afterOpenEdit: (detail) => {
      currentBusinessDatabaseProviderSnapshot.value = detail.provider
    },
    afterSave: async () => {
      await refreshBusinessDatabases()
      await refreshSemanticSourceStatuses()
    },
    afterDelete: async () => {
      await refreshBusinessDatabases()
      await refreshSemanticSourceStatuses()
    }
  })

  return {
    businessDatabases,
    currentBusinessDatabase,
    refreshBusinessDatabases,
    closeBusinessDatabaseDialog: businessDatabaseCrud.closeDialog,
    openCreateBusinessDatabaseDialog: businessDatabaseCrud.openCreateDialog,
    openEditBusinessDatabaseDialog: businessDatabaseCrud.openEditDialog,
    saveBusinessDatabase: businessDatabaseCrud.saveDialog,
    deleteBusinessDatabase: businessDatabaseCrud.deleteDialog
  }
}
