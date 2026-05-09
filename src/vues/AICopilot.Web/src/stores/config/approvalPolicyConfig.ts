import { ref } from 'vue'
import { CONFIG_STORE_MESSAGES } from '@/constants/messages'
import { CONFIG_READ_PERMISSIONS } from '@/security/permissions'
import { configService } from '@/services/configService'
import { normalizeToolNames } from '@/stores/config/configNormalizers'
import {
  createEmptyApprovalPolicyForm,
  toApprovalPolicyForm
} from '@/stores/configFormFactories'
import { useAuthStore } from '@/stores/authStore'
import { useDialogCrud } from '@/stores/useDialogCrud'
import type { ConfigDomainStates } from '@/stores/config/configStoreTypes'
import type { ApprovalPolicyFormModel, ApprovalPolicySummary } from '@/types/app'

export function useApprovalPolicyConfigDomain(states: ConfigDomainStates) {
  const authStore = useAuthStore()
  const approvalPolicies = ref<ApprovalPolicySummary[]>([])
  const currentApprovalPolicy = ref<ApprovalPolicyFormModel>(createEmptyApprovalPolicyForm())

  async function refreshApprovalPolicies() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.approvalPolicy)) {
      approvalPolicies.value = []
      return
    }

    states.loadingStates.approvalPolicy = true
    try {
      approvalPolicies.value = await configService.getApprovalPolicies()
    } finally {
      states.loadingStates.approvalPolicy = false
    }
  }

  const approvalPolicyCrud = useDialogCrud({
    domain: 'approvalPolicy',
    states,
    current: currentApprovalPolicy,
    messages: CONFIG_STORE_MESSAGES.approvalPolicy,
    createEmptyForm: createEmptyApprovalPolicyForm,
    toForm: toApprovalPolicyForm,
    loadDetail: configService.getApprovalPolicy,
    saveForm: async (form, mode) => {
      const payload = {
        ...form,
        name: form.name.trim(),
        description: form.description.trim(),
        targetName: form.targetName.trim(),
        toolNames: normalizeToolNames(form.toolNames)
      }

      if (mode === 'create') {
        await configService.createApprovalPolicy(payload)
      } else {
        await configService.updateApprovalPolicy(payload)
      }
    },
    deleteItem: async (id) => {
      await configService.deleteApprovalPolicy(id)
    },
    afterSave: refreshApprovalPolicies,
    afterDelete: refreshApprovalPolicies
  })

  return {
    approvalPolicies,
    currentApprovalPolicy,
    refreshApprovalPolicies,
    closeApprovalPolicyDialog: approvalPolicyCrud.closeDialog,
    openCreateApprovalPolicyDialog: approvalPolicyCrud.openCreateDialog,
    openEditApprovalPolicyDialog: approvalPolicyCrud.openEditDialog,
    saveApprovalPolicy: approvalPolicyCrud.saveDialog,
    deleteApprovalPolicy: approvalPolicyCrud.deleteDialog
  }
}
