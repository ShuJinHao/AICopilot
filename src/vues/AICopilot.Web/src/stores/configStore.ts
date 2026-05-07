import { defineStore } from 'pinia'
import { reactive, ref } from 'vue'
import { FORM_DEFAULTS } from '@/constants/formDefaults'
import { CONFIG_STORE_MESSAGES } from '@/constants/messages'
import { CONFIG_READ_PERMISSIONS } from '@/security/permissions'
import { configService } from '@/services/configService'
import {
  createEmptyApprovalPolicyForm,
  createEmptyBusinessDatabaseForm,
  createEmptyConversationTemplateForm,
  createEmptyLanguageModelForm,
  createEmptyMcpServerForm,
  toApprovalPolicyForm,
  toBusinessDatabaseForm,
  toConversationTemplateForm,
  toLanguageModelForm,
  toMcpServerForm
} from '@/stores/configFormFactories'
import { toStoreErrorMessage, useDialogCrud } from '@/stores/useDialogCrud'
import { useAuthStore } from '@/stores/authStore'
import type {
  ApprovalPolicyFormModel,
  ApprovalPolicySummary,
  BusinessDatabaseFormModel,
  BusinessDatabaseSummary,
  ConfigDialogMode,
  ConversationTemplateFormModel,
  ConversationTemplateSummary,
  LanguageModelFormModel,
  LanguageModelSummary,
  McpAllowedTool,
  McpServerFormModel,
  McpServerSummary,
  ProviderReliabilityConfig,
  SemanticSourceStatus
} from '@/types/app'

type EditableDomain =
  | 'languageModel'
  | 'conversationTemplate'
  | 'approvalPolicy'
  | 'businessDatabase'
  | 'mcpServer'

type LoadingDomain = EditableDomain | 'semanticSource'
type ReadOnlyConfigDomain = 'providerReliability'

function normalizeToolNames(toolNames: string[]) {
  return [...new Set(toolNames.map((item) => item.trim()).filter(Boolean))]
}

function normalizeMcpAllowedTools(tools: McpAllowedTool[]) {
  const normalized = new Map<string, McpAllowedTool>()

  for (const tool of tools) {
    const toolName = tool.toolName.trim()
    if (!toolName) {
      continue
    }

    const key = toolName.toLowerCase()
    if (!normalized.has(key)) {
      normalized.set(key, {
        toolName,
        externalSystemType: tool.externalSystemType ?? null,
        capabilityKind: tool.capabilityKind ?? null,
        riskLevel: tool.riskLevel ?? null,
        readOnlyDeclared: Boolean(tool.readOnlyDeclared),
        mcpReadOnlyHint: tool.mcpReadOnlyHint ?? null,
        mcpDestructiveHint: tool.mcpDestructiveHint ?? null,
        mcpIdempotentHint: tool.mcpIdempotentHint ?? null
      })
    }
  }

  return [...normalized.values()]
}

export const useConfigStore = defineStore('config', () => {
  const authStore = useAuthStore()
  const languageModels = ref<LanguageModelSummary[]>([])
  const conversationTemplates = ref<ConversationTemplateSummary[]>([])
  const approvalPolicies = ref<ApprovalPolicySummary[]>([])
  const businessDatabases = ref<BusinessDatabaseSummary[]>([])
  const mcpServers = ref<McpServerSummary[]>([])
  const semanticSourceStatuses = ref<SemanticSourceStatus[]>([])
  const providerReliability = ref<ProviderReliabilityConfig | null>(null)

  const isLoading = ref(false)
  const errorMessage = ref('')

  const loadingStates = reactive<Record<LoadingDomain | ReadOnlyConfigDomain, boolean>>({
    languageModel: false,
    conversationTemplate: false,
    approvalPolicy: false,
    businessDatabase: false,
    mcpServer: false,
    semanticSource: false,
    providerReliability: false
  })

  const dialogStates = reactive<Record<EditableDomain, boolean>>({
    languageModel: false,
    conversationTemplate: false,
    approvalPolicy: false,
    businessDatabase: false,
    mcpServer: false
  })

  const dialogModes = reactive<Record<EditableDomain, ConfigDialogMode>>({
    languageModel: 'create',
    conversationTemplate: 'create',
    approvalPolicy: 'create',
    businessDatabase: 'create',
    mcpServer: 'create'
  })

  const submittingStates = reactive<Record<EditableDomain, boolean>>({
    languageModel: false,
    conversationTemplate: false,
    approvalPolicy: false,
    businessDatabase: false,
    mcpServer: false
  })

  const actionErrors = reactive<Record<EditableDomain, string>>({
    languageModel: '',
    conversationTemplate: '',
    approvalPolicy: '',
    businessDatabase: '',
    mcpServer: ''
  })

  const currentLanguageModel = ref<LanguageModelFormModel>(createEmptyLanguageModelForm())
  const currentConversationTemplate = ref<ConversationTemplateFormModel>(
    createEmptyConversationTemplateForm()
  )
  const currentApprovalPolicy = ref<ApprovalPolicyFormModel>(createEmptyApprovalPolicyForm())
  const currentBusinessDatabase = ref<BusinessDatabaseFormModel>(createEmptyBusinessDatabaseForm())
  const currentMcpServer = ref<McpServerFormModel>(createEmptyMcpServerForm())
  const currentBusinessDatabaseProviderSnapshot = ref<number>(FORM_DEFAULTS.businessDatabase.provider)

  async function refreshLanguageModels() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.languageModel)) {
      languageModels.value = []
      return
    }

    loadingStates.languageModel = true
    try {
      languageModels.value = await configService.getLanguageModels()
    } finally {
      loadingStates.languageModel = false
    }
  }

  async function refreshProviderReliability() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.providerReliability)) {
      providerReliability.value = null
      return
    }

    loadingStates.providerReliability = true
    try {
      providerReliability.value = await configService.getProviderReliability()
    } finally {
      loadingStates.providerReliability = false
    }
  }

  async function refreshConversationTemplates() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.conversationTemplate)) {
      conversationTemplates.value = []
      return
    }

    loadingStates.conversationTemplate = true
    try {
      conversationTemplates.value = await configService.getConversationTemplates()
    } finally {
      loadingStates.conversationTemplate = false
    }
  }

  async function refreshApprovalPolicies() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.approvalPolicy)) {
      approvalPolicies.value = []
      return
    }

    loadingStates.approvalPolicy = true
    try {
      approvalPolicies.value = await configService.getApprovalPolicies()
    } finally {
      loadingStates.approvalPolicy = false
    }
  }

  async function refreshBusinessDatabases() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.businessDatabase)) {
      businessDatabases.value = []
      return
    }

    loadingStates.businessDatabase = true
    try {
      businessDatabases.value = await configService.getBusinessDatabases()
    } finally {
      loadingStates.businessDatabase = false
    }
  }

  async function refreshSemanticSourceStatuses() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.businessDatabase)) {
      semanticSourceStatuses.value = []
      return
    }

    loadingStates.semanticSource = true
    try {
      semanticSourceStatuses.value = await configService.getSemanticSourceStatuses()
    } finally {
      loadingStates.semanticSource = false
    }
  }

  async function refreshMcpServers() {
    if (!authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.mcpServer)) {
      mcpServers.value = []
      return
    }

    loadingStates.mcpServer = true
    try {
      mcpServers.value = await configService.getMcpServers()
    } finally {
      loadingStates.mcpServer = false
    }
  }

  async function refresh() {
    isLoading.value = true
    errorMessage.value = ''

    try {
      await Promise.all([
        refreshLanguageModels(),
        refreshProviderReliability(),
        refreshConversationTemplates(),
        refreshApprovalPolicies(),
        refreshBusinessDatabases(),
        refreshMcpServers(),
        refreshSemanticSourceStatuses()
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

  const languageModelCrud = useDialogCrud({
    domain: 'languageModel',
    states: { loadingStates, dialogStates, dialogModes, submittingStates, actionErrors },
    current: currentLanguageModel,
    messages: CONFIG_STORE_MESSAGES.languageModel,
    createEmptyForm: createEmptyLanguageModelForm,
    toForm: toLanguageModelForm,
    loadDetail: configService.getLanguageModel,
    saveForm: async (form, mode) => {
      const payload = {
        ...form,
        provider: form.provider.trim(),
        name: form.name.trim(),
        baseUrl: form.baseUrl.trim(),
        apiKey: form.apiKeyAction === 'replace' ? form.apiKey.trim() : '',
        clearApiKey: form.apiKeyAction === 'clear'
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

  const conversationTemplateCrud = useDialogCrud({
    domain: 'conversationTemplate',
    states: { loadingStates, dialogStates, dialogModes, submittingStates, actionErrors },
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

  const approvalPolicyCrud = useDialogCrud({
    domain: 'approvalPolicy',
    states: { loadingStates, dialogStates, dialogModes, submittingStates, actionErrors },
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

  const businessDatabaseCrud = useDialogCrud({
    domain: 'businessDatabase',
    states: { loadingStates, dialogStates, dialogModes, submittingStates, actionErrors },
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

  const mcpServerCrud = useDialogCrud({
    domain: 'mcpServer',
    states: { loadingStates, dialogStates, dialogModes, submittingStates, actionErrors },
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
        command: form.transportType === FORM_DEFAULTS.mcpServer.transportType ? form.command.trim() : '',
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

  const closeLanguageModelDialog = languageModelCrud.closeDialog
  const openCreateLanguageModelDialog = languageModelCrud.openCreateDialog
  const openEditLanguageModelDialog = languageModelCrud.openEditDialog
  const saveLanguageModel = languageModelCrud.saveDialog
  const deleteLanguageModel = languageModelCrud.deleteDialog

  const closeConversationTemplateDialog = conversationTemplateCrud.closeDialog
  const openCreateConversationTemplateDialog = conversationTemplateCrud.openCreateDialog
  const openEditConversationTemplateDialog = conversationTemplateCrud.openEditDialog
  const saveConversationTemplate = conversationTemplateCrud.saveDialog
  const deleteConversationTemplate = conversationTemplateCrud.deleteDialog

  const closeApprovalPolicyDialog = approvalPolicyCrud.closeDialog
  const openCreateApprovalPolicyDialog = approvalPolicyCrud.openCreateDialog
  const openEditApprovalPolicyDialog = approvalPolicyCrud.openEditDialog
  const saveApprovalPolicy = approvalPolicyCrud.saveDialog
  const deleteApprovalPolicy = approvalPolicyCrud.deleteDialog

  const closeBusinessDatabaseDialog = businessDatabaseCrud.closeDialog
  const openCreateBusinessDatabaseDialog = businessDatabaseCrud.openCreateDialog
  const openEditBusinessDatabaseDialog = businessDatabaseCrud.openEditDialog
  const saveBusinessDatabase = businessDatabaseCrud.saveDialog
  const deleteBusinessDatabase = businessDatabaseCrud.deleteDialog

  const closeMcpServerDialog = mcpServerCrud.closeDialog
  const openCreateMcpServerDialog = mcpServerCrud.openCreateDialog
  const openEditMcpServerDialog = mcpServerCrud.openEditDialog
  const saveMcpServer = mcpServerCrud.saveDialog
  const deleteMcpServer = mcpServerCrud.deleteDialog

  return {
    languageModels,
    conversationTemplates,
    approvalPolicies,
    businessDatabases,
    mcpServers,
    semanticSourceStatuses,
    providerReliability,
    isLoading,
    errorMessage,
    loadingStates,
    dialogStates,
    dialogModes,
    submittingStates,
    actionErrors,
    currentLanguageModel,
    currentConversationTemplate,
    currentApprovalPolicy,
    currentBusinessDatabase,
    currentMcpServer,
    refresh,
    refreshLanguageModels,
    refreshProviderReliability,
    refreshConversationTemplates,
    refreshApprovalPolicies,
    refreshBusinessDatabases,
    refreshMcpServers,
    refreshSemanticSourceStatuses,
    closeLanguageModelDialog,
    openCreateLanguageModelDialog,
    openEditLanguageModelDialog,
    saveLanguageModel,
    deleteLanguageModel,
    closeConversationTemplateDialog,
    openCreateConversationTemplateDialog,
    openEditConversationTemplateDialog,
    saveConversationTemplate,
    deleteConversationTemplate,
    closeApprovalPolicyDialog,
    openCreateApprovalPolicyDialog,
    openEditApprovalPolicyDialog,
    saveApprovalPolicy,
    deleteApprovalPolicy,
    closeBusinessDatabaseDialog,
    openCreateBusinessDatabaseDialog,
    openEditBusinessDatabaseDialog,
    saveBusinessDatabase,
    deleteBusinessDatabase,
    closeMcpServerDialog,
    openCreateMcpServerDialog,
    openEditMcpServerDialog,
    saveMcpServer,
    deleteMcpServer
  }
})
