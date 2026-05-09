import { ref } from 'vue'
import { FORM_DEFAULTS } from '@/constants/formDefaults'
import { CONFIG_STORE_MESSAGES } from '@/constants/messages'
import { CONFIG_READ_PERMISSIONS } from '@/security/permissions'
import { configService } from '@/services/configService'
import { normalizeMcpAllowedTools } from '@/stores/config/configNormalizers'
import { createEmptyMcpServerForm, toMcpServerForm } from '@/stores/configFormFactories'
import { useAuthStore } from '@/stores/authStore'
import { useDialogCrud } from '@/stores/useDialogCrud'
import type { ConfigDomainStates } from '@/stores/config/configStoreTypes'
import type { McpServerFormModel, McpServerSummary } from '@/types/app'

export function useMcpServerConfigDomain(states: ConfigDomainStates) {
  const authStore = useAuthStore()
  const mcpServers = ref<McpServerSummary[]>([])
  const currentMcpServer = ref<McpServerFormModel>(createEmptyMcpServerForm())

  async function refreshMcpServers() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.mcpServer)) {
      mcpServers.value = []
      return
    }

    states.loadingStates.mcpServer = true
    try {
      mcpServers.value = await configService.getMcpServers()
    } finally {
      states.loadingStates.mcpServer = false
    }
  }

  const mcpServerCrud = useDialogCrud({
    domain: 'mcpServer',
    states,
    current: currentMcpServer,
    messages: CONFIG_STORE_MESSAGES.mcpServer,
    createEmptyForm: createEmptyMcpServerForm,
    toForm: toMcpServerForm,
    loadDetail: configService.getMcpServer,
    saveForm: async (form, mode) => {
      const payload = {
        ...form,
        name: form.name.trim(),
        description: form.description.trim(),
        command:
          form.transportType === FORM_DEFAULTS.mcpServer.transportType ? form.command.trim() : '',
        arguments: form.arguments.trim(),
        allowedTools: normalizeMcpAllowedTools(form.allowedTools)
      }

      if (mode === 'create') {
        await configService.createMcpServer(payload)
      } else {
        await configService.updateMcpServer(payload)
      }
    },
    deleteItem: async (id) => {
      await configService.deleteMcpServer(id)
    },
    afterSave: refreshMcpServers,
    afterDelete: refreshMcpServers
  })

  return {
    mcpServers,
    currentMcpServer,
    refreshMcpServers,
    closeMcpServerDialog: mcpServerCrud.closeDialog,
    openCreateMcpServerDialog: mcpServerCrud.openCreateDialog,
    openEditMcpServerDialog: mcpServerCrud.openEditDialog,
    saveMcpServer: mcpServerCrud.saveDialog,
    deleteMcpServer: mcpServerCrud.deleteDialog
  }
}
