import { defineStore } from 'pinia'
import { reactive, ref } from 'vue'
import { ApiError, getProblemDetails } from '@/services/apiClient'
import { CONFIG_READ_PERMISSIONS } from '@/security/permissions'
import { configService } from '@/services/configService'
import { useAuthStore } from '@/stores/authStore'
import type {
  ApprovalPolicyDetail,
  ApprovalPolicyFormModel,
  ApprovalPolicySummary,
  BusinessDatabaseDetail,
  BusinessDatabaseFormModel,
  BusinessDatabaseSummary,
  ConfigDialogMode,
  ConversationTemplateDetail,
  ConversationTemplateFormModel,
  ConversationTemplateSummary,
  LanguageModelDetail,
  LanguageModelFormModel,
  LanguageModelSummary,
  McpServerDetail,
  McpServerFormModel,
  McpServerSummary,
  SemanticSourceStatus
} from '@/types/app'

type EditableDomain =
  | 'languageModel'
  | 'conversationTemplate'
  | 'approvalPolicy'
  | 'businessDatabase'
  | 'mcpServer'

type LoadingDomain = EditableDomain | 'semanticSource'

function createEmptyLanguageModelForm(): LanguageModelFormModel {
  return {
    provider: 'OpenAI',
    name: '',
    baseUrl: '',
    apiKey: '',
    apiKeyAction: 'replace',
    clearApiKey: false,
    maxTokens: 2048,
    temperature: 0.7,
    hasApiKey: false,
    apiKeyMasked: null
  }
}

function createEmptyConversationTemplateForm(): ConversationTemplateFormModel {
  return {
    name: '',
    description: '',
    systemPrompt: '',
    modelId: '',
    maxTokens: null,
    temperature: null,
    isEnabled: true
  }
}

function createEmptyApprovalPolicyForm(): ApprovalPolicyFormModel {
  return {
    name: '',
    description: '',
    targetType: 'Plugin',
    targetName: '',
    toolNames: [],
    isEnabled: true,
    requiresOnsiteAttestation: false
  }
}

function createEmptyBusinessDatabaseForm(): BusinessDatabaseFormModel {
  return {
    name: '',
    description: '',
    connectionString: '',
    provider: 1,
    isEnabled: true,
    isReadOnly: true,
    externalSystemType: 0,
    readOnlyCredentialVerified: false,
    hasConnectionString: false,
    connectionStringMasked: null
  }
}

function createEmptyMcpServerForm(): McpServerFormModel {
  return {
    name: '',
    description: '',
    transportType: 1,
    command: 'dotnet',
    arguments: '',
    chatExposureMode: 0,
    allowedToolNames: [],
    externalSystemType: 0,
    capabilityKind: 1,
    riskLevel: 1,
    isEnabled: true,
    hasArguments: false,
    argumentsMasked: null,
    originalTransportType: 1
  }
}

function toLanguageModelForm(detail: LanguageModelDetail): LanguageModelFormModel {
  return {
    id: detail.id,
    provider: detail.provider,
    name: detail.name,
    baseUrl: detail.baseUrl,
    apiKey: '',
    apiKeyAction: 'keep',
    clearApiKey: false,
    maxTokens: detail.maxTokens,
    temperature: detail.temperature,
    hasApiKey: detail.hasApiKey,
    apiKeyMasked: detail.apiKeyMasked
  }
}

function toConversationTemplateForm(
  detail: ConversationTemplateDetail
): ConversationTemplateFormModel {
  return {
    id: detail.id,
    name: detail.name,
    description: detail.description,
    systemPrompt: detail.systemPrompt,
    modelId: detail.modelId,
    maxTokens: detail.maxTokens ?? null,
    temperature: detail.temperature ?? null,
    isEnabled: detail.isEnabled
  }
}

function toApprovalPolicyForm(detail: ApprovalPolicyDetail): ApprovalPolicyFormModel {
  return {
    id: detail.id,
    name: detail.name,
    description: detail.description ?? '',
    targetType: detail.targetType,
    targetName: detail.targetName,
    toolNames: [...detail.toolNames],
    isEnabled: detail.isEnabled,
    requiresOnsiteAttestation: detail.requiresOnsiteAttestation
  }
}

function toBusinessDatabaseForm(detail: BusinessDatabaseDetail): BusinessDatabaseFormModel {
  return {
    id: detail.id,
    name: detail.name,
    description: detail.description,
    connectionString: '',
    provider: detail.provider,
    isEnabled: detail.isEnabled,
    isReadOnly: true,
    externalSystemType: detail.externalSystemType,
    readOnlyCredentialVerified: detail.readOnlyCredentialVerified,
    hasConnectionString: detail.hasConnectionString,
    connectionStringMasked: detail.connectionStringMasked
  }
}

function toMcpServerForm(detail: McpServerDetail): McpServerFormModel {
  return {
    id: detail.id,
    name: detail.name,
    description: detail.description,
    transportType: detail.transportType,
    command: detail.command ?? '',
    arguments: '',
    chatExposureMode: detail.chatExposureMode,
    allowedToolNames: [...detail.allowedToolNames],
    externalSystemType: detail.externalSystemType,
    capabilityKind: detail.capabilityKind,
    riskLevel: detail.riskLevel,
    isEnabled: detail.isEnabled,
    hasArguments: detail.hasArguments,
    argumentsMasked: detail.argumentsMasked,
    originalTransportType: detail.transportType
  }
}

function normalizeToolNames(toolNames: string[]) {
  return [...new Set(toolNames.map((item) => item.trim()).filter(Boolean))]
}

function toErrorMessage(error: unknown, fallback: string, forbiddenMessage: string) {
  if (error instanceof ApiError && error.status === 403) {
    return forbiddenMessage
  }

  if (error instanceof ApiError) {
    const problem = getProblemDetails(error.details)
    return problem?.detail || problem?.title || fallback
  }

  return fallback
}

export const useConfigStore = defineStore('config', () => {
  const authStore = useAuthStore()
  const languageModels = ref<LanguageModelSummary[]>([])
  const conversationTemplates = ref<ConversationTemplateSummary[]>([])
  const approvalPolicies = ref<ApprovalPolicySummary[]>([])
  const businessDatabases = ref<BusinessDatabaseSummary[]>([])
  const mcpServers = ref<McpServerSummary[]>([])
  const semanticSourceStatuses = ref<SemanticSourceStatus[]>([])

  const isLoading = ref(false)
  const errorMessage = ref('')

  const loadingStates = reactive<Record<LoadingDomain, boolean>>({
    languageModel: false,
    conversationTemplate: false,
    approvalPolicy: false,
    businessDatabase: false,
    mcpServer: false,
    semanticSource: false
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
  const currentBusinessDatabaseProviderSnapshot = ref(1)

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
        refreshConversationTemplates(),
        refreshApprovalPolicies(),
        refreshBusinessDatabases(),
        refreshMcpServers(),
        refreshSemanticSourceStatuses()
      ])
    } catch (error) {
      errorMessage.value = toErrorMessage(
        error,
        '配置页面加载失败，请稍后重试。',
        '当前账号没有查看配置的权限。'
      )
      throw error
    } finally {
      isLoading.value = false
    }
  }

  function closeLanguageModelDialog() {
    dialogStates.languageModel = false
    dialogModes.languageModel = 'create'
    actionErrors.languageModel = ''
    currentLanguageModel.value = createEmptyLanguageModelForm()
  }

  function openCreateLanguageModelDialog() {
    actionErrors.languageModel = ''
    dialogModes.languageModel = 'create'
    currentLanguageModel.value = createEmptyLanguageModelForm()
    dialogStates.languageModel = true
  }

  async function openEditLanguageModelDialog(id: string) {
    loadingStates.languageModel = true
    actionErrors.languageModel = ''

    try {
      const detail = await configService.getLanguageModel(id)
      currentLanguageModel.value = toLanguageModelForm(detail)
      dialogModes.languageModel = 'edit'
      dialogStates.languageModel = true
    } catch (error) {
      actionErrors.languageModel = toErrorMessage(
        error,
        '加载模型详情失败，请稍后重试。',
        '当前账号没有查看模型详情的权限。'
      )
      throw error
    } finally {
      loadingStates.languageModel = false
    }
  }

  async function saveLanguageModel() {
    submittingStates.languageModel = true
    actionErrors.languageModel = ''

    try {
      const payload = {
        ...currentLanguageModel.value,
        provider: currentLanguageModel.value.provider.trim(),
        name: currentLanguageModel.value.name.trim(),
        baseUrl: currentLanguageModel.value.baseUrl.trim(),
        apiKey:
          currentLanguageModel.value.apiKeyAction === 'replace'
            ? currentLanguageModel.value.apiKey.trim()
            : '',
        clearApiKey: currentLanguageModel.value.apiKeyAction === 'clear'
      }

      if (dialogModes.languageModel === 'create') {
        await configService.createLanguageModel(payload)
      } else {
        await configService.updateLanguageModel(payload)
      }

      await refreshLanguageModels()
      closeLanguageModelDialog()
    } catch (error) {
      actionErrors.languageModel = toErrorMessage(
        error,
        '保存模型失败，请稍后重试。',
        '当前账号没有管理模型的权限。'
      )
      throw error
    } finally {
      submittingStates.languageModel = false
    }
  }

  async function deleteLanguageModel(id: string) {
    actionErrors.languageModel = ''

    try {
      await configService.deleteLanguageModel(id)
      await refreshLanguageModels()
    } catch (error) {
      actionErrors.languageModel = toErrorMessage(
        error,
        '删除模型失败，请稍后重试。',
        '当前账号没有删除模型的权限。'
      )
      throw error
    }
  }

  function closeConversationTemplateDialog() {
    dialogStates.conversationTemplate = false
    dialogModes.conversationTemplate = 'create'
    actionErrors.conversationTemplate = ''
    currentConversationTemplate.value = createEmptyConversationTemplateForm()
  }

  function openCreateConversationTemplateDialog() {
    actionErrors.conversationTemplate = ''
    dialogModes.conversationTemplate = 'create'
    currentConversationTemplate.value = createEmptyConversationTemplateForm()
    dialogStates.conversationTemplate = true
  }

  async function openEditConversationTemplateDialog(id: string) {
    loadingStates.conversationTemplate = true
    actionErrors.conversationTemplate = ''

    try {
      const detail = await configService.getConversationTemplate(id)
      currentConversationTemplate.value = toConversationTemplateForm(detail)
      dialogModes.conversationTemplate = 'edit'
      dialogStates.conversationTemplate = true
    } catch (error) {
      actionErrors.conversationTemplate = toErrorMessage(
        error,
        '加载模板详情失败，请稍后重试。',
        '当前账号没有查看模板详情的权限。'
      )
      throw error
    } finally {
      loadingStates.conversationTemplate = false
    }
  }

  async function saveConversationTemplate() {
    submittingStates.conversationTemplate = true
    actionErrors.conversationTemplate = ''

    try {
      const payload = {
        ...currentConversationTemplate.value,
        name: currentConversationTemplate.value.name.trim(),
        description: currentConversationTemplate.value.description.trim(),
        systemPrompt: currentConversationTemplate.value.systemPrompt.trim()
      }

      if (dialogModes.conversationTemplate === 'create') {
        await configService.createConversationTemplate(payload)
      } else {
        await configService.updateConversationTemplate(payload)
      }

      await refreshConversationTemplates()
      closeConversationTemplateDialog()
    } catch (error) {
      actionErrors.conversationTemplate = toErrorMessage(
        error,
        '保存模板失败，请稍后重试。',
        '当前账号没有管理模板的权限。'
      )
      throw error
    } finally {
      submittingStates.conversationTemplate = false
    }
  }

  async function deleteConversationTemplate(id: string) {
    actionErrors.conversationTemplate = ''

    try {
      await configService.deleteConversationTemplate(id)
      await refreshConversationTemplates()
    } catch (error) {
      actionErrors.conversationTemplate = toErrorMessage(
        error,
        '删除模板失败，请稍后重试。',
        '当前账号没有删除模板的权限。'
      )
      throw error
    }
  }

  function closeApprovalPolicyDialog() {
    dialogStates.approvalPolicy = false
    dialogModes.approvalPolicy = 'create'
    actionErrors.approvalPolicy = ''
    currentApprovalPolicy.value = createEmptyApprovalPolicyForm()
  }

  function openCreateApprovalPolicyDialog() {
    actionErrors.approvalPolicy = ''
    dialogModes.approvalPolicy = 'create'
    currentApprovalPolicy.value = createEmptyApprovalPolicyForm()
    dialogStates.approvalPolicy = true
  }

  async function openEditApprovalPolicyDialog(id: string) {
    loadingStates.approvalPolicy = true
    actionErrors.approvalPolicy = ''

    try {
      const detail = await configService.getApprovalPolicy(id)
      currentApprovalPolicy.value = toApprovalPolicyForm(detail)
      dialogModes.approvalPolicy = 'edit'
      dialogStates.approvalPolicy = true
    } catch (error) {
      actionErrors.approvalPolicy = toErrorMessage(
        error,
        '加载审批策略详情失败，请稍后重试。',
        '当前账号没有查看审批策略详情的权限。'
      )
      throw error
    } finally {
      loadingStates.approvalPolicy = false
    }
  }

  async function saveApprovalPolicy() {
    submittingStates.approvalPolicy = true
    actionErrors.approvalPolicy = ''

    try {
      const payload = {
        ...currentApprovalPolicy.value,
        name: currentApprovalPolicy.value.name.trim(),
        description: currentApprovalPolicy.value.description.trim(),
        targetName: currentApprovalPolicy.value.targetName.trim(),
        toolNames: normalizeToolNames(currentApprovalPolicy.value.toolNames)
      }

      if (dialogModes.approvalPolicy === 'create') {
        await configService.createApprovalPolicy(payload)
      } else {
        await configService.updateApprovalPolicy(payload)
      }

      await refreshApprovalPolicies()
      closeApprovalPolicyDialog()
    } catch (error) {
      actionErrors.approvalPolicy = toErrorMessage(
        error,
        '保存审批策略失败，请稍后重试。',
        '当前账号没有管理审批策略的权限。'
      )
      throw error
    } finally {
      submittingStates.approvalPolicy = false
    }
  }

  async function deleteApprovalPolicy(id: string) {
    actionErrors.approvalPolicy = ''

    try {
      await configService.deleteApprovalPolicy(id)
      await refreshApprovalPolicies()
    } catch (error) {
      actionErrors.approvalPolicy = toErrorMessage(
        error,
        '删除审批策略失败，请稍后重试。',
        '当前账号没有删除审批策略的权限。'
      )
      throw error
    }
  }

  function closeBusinessDatabaseDialog() {
    dialogStates.businessDatabase = false
    dialogModes.businessDatabase = 'create'
    actionErrors.businessDatabase = ''
    currentBusinessDatabase.value = createEmptyBusinessDatabaseForm()
    currentBusinessDatabaseProviderSnapshot.value = 1
  }

  function openCreateBusinessDatabaseDialog() {
    actionErrors.businessDatabase = ''
    dialogModes.businessDatabase = 'create'
    currentBusinessDatabase.value = createEmptyBusinessDatabaseForm()
    currentBusinessDatabaseProviderSnapshot.value = 1
    dialogStates.businessDatabase = true
  }

  async function openEditBusinessDatabaseDialog(id: string) {
    loadingStates.businessDatabase = true
    actionErrors.businessDatabase = ''

    try {
      const detail = await configService.getBusinessDatabase(id)
      currentBusinessDatabase.value = toBusinessDatabaseForm(detail)
      currentBusinessDatabaseProviderSnapshot.value = detail.provider
      dialogModes.businessDatabase = 'edit'
      dialogStates.businessDatabase = true
    } catch (error) {
      actionErrors.businessDatabase = toErrorMessage(
        error,
        '加载业务库详情失败，请稍后重试。',
        '当前账号没有查看业务库详情的权限。'
      )
      throw error
    } finally {
      loadingStates.businessDatabase = false
    }
  }

  async function saveBusinessDatabase() {
    submittingStates.businessDatabase = true
    actionErrors.businessDatabase = ''

    try {
      const connectionString = currentBusinessDatabase.value.connectionString.trim()
      const isEditing = dialogModes.businessDatabase === 'edit'
      const provider =
        isEditing && connectionString.length === 0
          ? currentBusinessDatabaseProviderSnapshot.value
          : currentBusinessDatabase.value.provider

      const payload = {
        ...currentBusinessDatabase.value,
        name: currentBusinessDatabase.value.name.trim(),
        description: currentBusinessDatabase.value.description.trim(),
        connectionString,
        provider,
        isReadOnly: true,
        externalSystemType: currentBusinessDatabase.value.externalSystemType,
        readOnlyCredentialVerified: currentBusinessDatabase.value.readOnlyCredentialVerified
      }

      if (dialogModes.businessDatabase === 'create') {
        await configService.createBusinessDatabase(payload)
      } else {
        await configService.updateBusinessDatabase(payload)
      }

      await refreshBusinessDatabases()
      await refreshSemanticSourceStatuses()
      closeBusinessDatabaseDialog()
    } catch (error) {
      actionErrors.businessDatabase = toErrorMessage(
        error,
        '保存业务库失败，请稍后重试。',
        '当前账号没有管理业务库的权限。'
      )
      throw error
    } finally {
      submittingStates.businessDatabase = false
    }
  }

  async function deleteBusinessDatabase(id: string) {
    actionErrors.businessDatabase = ''

    try {
      await configService.deleteBusinessDatabase(id)
      await refreshBusinessDatabases()
      await refreshSemanticSourceStatuses()
    } catch (error) {
      actionErrors.businessDatabase = toErrorMessage(
        error,
        '删除业务库失败，请稍后重试。',
        '当前账号没有删除业务库的权限。'
      )
      throw error
    }
  }

  function closeMcpServerDialog() {
    dialogStates.mcpServer = false
    dialogModes.mcpServer = 'create'
    actionErrors.mcpServer = ''
    currentMcpServer.value = createEmptyMcpServerForm()
  }

  function openCreateMcpServerDialog() {
    actionErrors.mcpServer = ''
    dialogModes.mcpServer = 'create'
    currentMcpServer.value = createEmptyMcpServerForm()
    dialogStates.mcpServer = true
  }

  async function openEditMcpServerDialog(id: string) {
    loadingStates.mcpServer = true
    actionErrors.mcpServer = ''

    try {
      const detail = await configService.getMcpServer(id)
      currentMcpServer.value = toMcpServerForm(detail)
      dialogModes.mcpServer = 'edit'
      dialogStates.mcpServer = true
    } catch (error) {
      actionErrors.mcpServer = toErrorMessage(
        error,
        '加载 MCP 服务详情失败，请稍后重试。',
        '当前账号没有查看 MCP 服务详情的权限。'
      )
      throw error
    } finally {
      loadingStates.mcpServer = false
    }
  }

  async function saveMcpServer() {
    submittingStates.mcpServer = true
    actionErrors.mcpServer = ''

    try {
      const payload = {
        ...currentMcpServer.value,
        name: currentMcpServer.value.name.trim(),
        description: currentMcpServer.value.description.trim(),
        command:
          currentMcpServer.value.transportType === 1
            ? currentMcpServer.value.command.trim()
            : '',
        arguments: currentMcpServer.value.arguments.trim(),
        allowedToolNames: normalizeToolNames(currentMcpServer.value.allowedToolNames)
      }

      if (dialogModes.mcpServer === 'create') {
        await configService.createMcpServer(payload)
      } else {
        await configService.updateMcpServer(payload)
      }

      await refreshMcpServers()
      closeMcpServerDialog()
    } catch (error) {
      actionErrors.mcpServer = toErrorMessage(
        error,
        '保存 MCP 服务失败，请稍后重试。',
        '当前账号没有管理 MCP 服务的权限。'
      )
      throw error
    } finally {
      submittingStates.mcpServer = false
    }
  }

  async function deleteMcpServer(id: string) {
    actionErrors.mcpServer = ''

    try {
      await configService.deleteMcpServer(id)
      await refreshMcpServers()
    } catch (error) {
      actionErrors.mcpServer = toErrorMessage(
        error,
        '删除 MCP 服务失败，请稍后重试。',
        '当前账号没有删除 MCP 服务的权限。'
      )
      throw error
    }
  }

  return {
    languageModels,
    conversationTemplates,
    approvalPolicies,
    businessDatabases,
    mcpServers,
    semanticSourceStatuses,
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
