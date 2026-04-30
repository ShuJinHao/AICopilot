<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import type { FormInstance, FormRules } from 'element-plus'
import { ElMessage, ElMessageBox } from 'element-plus'
import AppShell from '@/components/layout/AppShell.vue'
import {
  CONFIG_READ_PERMISSIONS,
  CONFIG_WRITE_PERMISSIONS
} from '@/security/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useConfigStore } from '@/stores/configStore'
import type {
  ApprovalPolicyFormModel,
  BusinessDatabaseFormModel,
  ConversationTemplateFormModel,
  LanguageModelFormModel,
  McpServerFormModel,
  SemanticSourceStatus
} from '@/types/app'

const authStore = useAuthStore()
const configStore = useConfigStore()

const languageModelFormRef = ref<FormInstance>()
const conversationTemplateFormRef = ref<FormInstance>()
const approvalPolicyFormRef = ref<FormInstance>()
const businessDatabaseFormRef = ref<FormInstance>()
const mcpServerFormRef = ref<FormInstance>()
const toolNameDraft = ref('')
const mcpToolNameDraft = ref('')

const readySemanticSources = computed(
  () => configStore.semanticSourceStatuses.filter((item) => item.status === 'Ready').length
)

const hasLanguageModels = computed(() => configStore.languageModels.length > 0)
const isBusinessDatabaseProviderLocked = computed(
  () =>
    configStore.dialogModes.businessDatabase === 'edit' &&
    configStore.currentBusinessDatabase.hasConnectionString &&
    configStore.currentBusinessDatabase.connectionString.trim().length === 0
)
const isLanguageModelSecretActionEditable = computed(
  () => configStore.dialogModes.languageModel === 'edit'
)
const isLanguageModelApiKeyInputEnabled = computed(
  () =>
    configStore.dialogModes.languageModel === 'create' ||
    configStore.currentLanguageModel.apiKeyAction === 'replace'
)
const languageModelApiKeyPlaceholder = computed(() => {
  if (configStore.dialogModes.languageModel === 'create') {
    return '请输入模型 API Key'
  }

  switch (configStore.currentLanguageModel.apiKeyAction) {
    case 'replace':
      return '请输入新的 API Key'
    case 'clear':
      return '当前将清空已保存密钥'
    default:
      return '保留现有密钥，无需重新输入'
  }
})

const canReadLanguageModels = computed(() =>
  authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.languageModel)
)
const canReadConversationTemplates = computed(() =>
  authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.conversationTemplate)
)
const canReadApprovalPolicies = computed(() =>
  authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.approvalPolicy)
)
const canReadBusinessDatabases = computed(() =>
  authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.businessDatabase)
)
const canReadMcpServers = computed(() => authStore.hasAnyPermission(CONFIG_READ_PERMISSIONS.mcpServer))
const canReadSemanticSources = computed(() => canReadBusinessDatabases.value)

const canCreateLanguageModels = computed(() =>
  authStore.hasPermission(CONFIG_WRITE_PERMISSIONS.languageModel.create)
)
const canUpdateLanguageModels = computed(() =>
  authStore.hasPermission(CONFIG_WRITE_PERMISSIONS.languageModel.update)
)
const canDeleteLanguageModels = computed(() =>
  authStore.hasPermission(CONFIG_WRITE_PERMISSIONS.languageModel.delete)
)

const canCreateConversationTemplates = computed(() =>
  authStore.hasPermission(CONFIG_WRITE_PERMISSIONS.conversationTemplate.create)
)
const canUpdateConversationTemplates = computed(() =>
  authStore.hasPermission(CONFIG_WRITE_PERMISSIONS.conversationTemplate.update)
)
const canDeleteConversationTemplates = computed(() =>
  authStore.hasPermission(CONFIG_WRITE_PERMISSIONS.conversationTemplate.delete)
)

const canCreateApprovalPolicies = computed(() =>
  authStore.hasPermission(CONFIG_WRITE_PERMISSIONS.approvalPolicy.create)
)
const canUpdateApprovalPolicies = computed(() =>
  authStore.hasPermission(CONFIG_WRITE_PERMISSIONS.approvalPolicy.update)
)
const canDeleteApprovalPolicies = computed(() =>
  authStore.hasPermission(CONFIG_WRITE_PERMISSIONS.approvalPolicy.delete)
)

const canCreateBusinessDatabases = computed(() =>
  authStore.hasPermission(CONFIG_WRITE_PERMISSIONS.businessDatabase.create)
)
const canUpdateBusinessDatabases = computed(() =>
  authStore.hasPermission(CONFIG_WRITE_PERMISSIONS.businessDatabase.update)
)
const canDeleteBusinessDatabases = computed(() =>
  authStore.hasPermission(CONFIG_WRITE_PERMISSIONS.businessDatabase.delete)
)
const canCreateMcpServers = computed(() =>
  authStore.hasPermission(CONFIG_WRITE_PERMISSIONS.mcpServer.create)
)
const canUpdateMcpServers = computed(() =>
  authStore.hasPermission(CONFIG_WRITE_PERMISSIONS.mcpServer.update)
)
const canDeleteMcpServers = computed(() =>
  authStore.hasPermission(CONFIG_WRITE_PERMISSIONS.mcpServer.delete)
)

const approvalTargetOptions = [
  { label: 'Plugin', value: 'Plugin' },
  { label: 'MCP Server', value: 'McpServer' }
] as const

const databaseProviderOptions = [
  { label: 'PostgreSql', value: 1 },
  { label: 'SqlServer', value: 2 },
  { label: 'MySql', value: 3 }
]

const mcpTransportOptions = [
  { label: 'Stdio', value: 1 },
  { label: 'SSE', value: 2 }
]

const chatExposureOptions = [
  { label: '禁用', value: 0 },
  { label: '只观察', value: 1 },
  { label: '建议模式', value: 2 },
  { label: '控制模式', value: 3 }
]

const languageModelRules: FormRules<LanguageModelFormModel> = {
  provider: [{ required: true, message: '请输入模型提供方', trigger: 'blur' }],
  name: [{ required: true, message: '请输入模型名称', trigger: 'blur' }],
  baseUrl: [{ required: true, message: '请输入模型地址', trigger: 'blur' }],
  maxTokens: [{ required: true, message: '请输入最大令牌数', trigger: 'change' }],
  temperature: [{ required: true, message: '请输入温度参数', trigger: 'change' }]
}

const conversationTemplateRules: FormRules<ConversationTemplateFormModel> = {
  name: [{ required: true, message: '请输入模板名称', trigger: 'blur' }],
  description: [{ required: true, message: '请输入模板说明', trigger: 'blur' }],
  systemPrompt: [{ required: true, message: '请输入系统提示词', trigger: 'blur' }],
  modelId: [{ required: true, message: '请选择关联模型', trigger: 'change' }]
}

const approvalPolicyRules: FormRules<ApprovalPolicyFormModel> = {
  name: [{ required: true, message: '请输入审批策略名称', trigger: 'blur' }],
  targetType: [{ required: true, message: '请选择目标类型', trigger: 'change' }],
  targetName: [{ required: true, message: '请输入目标名称', trigger: 'blur' }]
}

const businessDatabaseRules: FormRules<BusinessDatabaseFormModel> = {
  name: [{ required: true, message: '请输入业务库名称', trigger: 'blur' }],
  description: [{ required: true, message: '请输入业务库说明', trigger: 'blur' }],
  provider: [{ required: true, message: '请选择数据库类型', trigger: 'change' }],
  connectionString: [
    {
      validator: (_, value, callback) => {
        const isCreate = configStore.dialogModes.businessDatabase === 'create'
        const hasExisting = configStore.currentBusinessDatabase.hasConnectionString

        if (isCreate && !String(value ?? '').trim()) {
          callback(new Error('请输入连接字符串'))
          return
        }

        if (!isCreate && !hasExisting && !String(value ?? '').trim()) {
          callback(new Error('请输入连接字符串'))
          return
        }

        callback()
      },
      trigger: 'blur'
    }
  ]
}

const mcpServerRules: FormRules<McpServerFormModel> = {
  name: [{ required: true, message: '请输入 MCP 服务名称', trigger: 'blur' }],
  description: [{ required: true, message: '请输入 MCP 服务说明', trigger: 'blur' }],
  transportType: [{ required: true, message: '请选择传输类型', trigger: 'change' }],
  command: [
    {
      validator: (_, value, callback) => {
        if (configStore.currentMcpServer.transportType === 1 && !String(value ?? '').trim()) {
          callback(new Error('Stdio 服务必须配置启动命令'))
          return
        }

        callback()
      },
      trigger: 'blur'
    }
  ],
  arguments: [
    {
      validator: (_, value, callback) => {
        const isCreate = configStore.dialogModes.mcpServer === 'create'
        const transportChanged =
          configStore.currentMcpServer.originalTransportType !== undefined &&
          configStore.currentMcpServer.transportType !==
            configStore.currentMcpServer.originalTransportType
        const hasExisting = configStore.currentMcpServer.hasArguments && !transportChanged

        if ((isCreate || !hasExisting) && !String(value ?? '').trim()) {
          callback(new Error('请输入启动参数或 SSE 地址'))
          return
        }

        callback()
      },
      trigger: 'blur'
    }
  ]
}

function providerLabel(provider: number) {
  return databaseProviderOptions.find((item) => item.value === provider)?.label ?? `Provider-${provider}`
}

function mcpTransportLabel(transportType: number) {
  return mcpTransportOptions.find((item) => item.value === transportType)?.label ?? `Transport-${transportType}`
}

function chatExposureLabel(mode: number) {
  return chatExposureOptions.find((item) => item.value === mode)?.label ?? `Mode-${mode}`
}

function chatExposureTagType(mode: number) {
  switch (mode) {
    case 1:
    case 2:
      return 'success'
    case 3:
      return 'danger'
    default:
      return 'info'
  }
}

function semanticStatusType(status: SemanticSourceStatus['status']) {
  switch (status) {
    case 'Ready':
      return 'success'
    case 'Disabled':
    case 'NotReadOnly':
      return 'warning'
    default:
      return 'danger'
  }
}

function semanticValidationTagType(validated: boolean | null) {
  if (validated === null) {
    return 'info'
  }

  return validated ? 'success' : 'danger'
}

function semanticSourceExistsState(status: SemanticSourceStatus['status'], sourceExists: boolean) {
  if (status === 'Ready' || status === 'SourceNotFound' || status === 'FieldMismatch') {
    return sourceExists
  }

  return null
}

function semanticProviderMatchedState(
  status: SemanticSourceStatus['status'],
  providerMatched: boolean
) {
  if (
    status === 'Ready' ||
    status === 'SourceNotFound' ||
    status === 'FieldMismatch' ||
    status === 'ProviderMismatch'
  ) {
    return providerMatched
  }

  return null
}

function semanticValidationText(validated: boolean | null, trueText: string, falseText: string) {
  if (validated === null) {
    return '未校验'
  }

  return validated ? trueText : falseText
}

function semanticStatusDescription(item: SemanticSourceStatus) {
  switch (item.status) {
    case 'Ready':
      return '映射配置、业务库提供程序、只读源与五域字段契约都已对齐。'
    case 'MissingMapping':
      return '当前 target 还没有配置 SemanticMappings，未来真实源无法切换。'
    case 'DatabaseNotFound':
      return '逻辑业务库未找到，请确认 DeviceSemanticReadonly 是否已创建并绑定真实只读源。'
    case 'Disabled':
      return '业务库已被停用，状态页不会继续探测底层只读源。'
    case 'NotReadOnly':
      return '业务库不是只读配置，AICopilot 不会把它视为可接入的真实源。'
    case 'ProviderMismatch':
      return '业务库 provider 与语义映射 provider 不一致，请先对齐驱动类型。'
    case 'SourceNotFound':
      return '映射指向的 SourceName / FromClause 无法解析为可查询只读源。'
    case 'FieldMismatch':
      return item.missingRequiredFields.length > 0
        ? `源已存在，但五域字段契约仍缺失：${item.missingRequiredFields.join(', ')}。`
        : '源已存在，但字段契约未完全对齐。'
    default:
      return '当前语义源状态未知，请检查映射配置与业务库绑定。'
  }
}

function enabledTagType(enabled: boolean) {
  return enabled ? 'success' : 'info'
}

function readonlyTagType(readOnly: boolean) {
  return readOnly ? 'success' : 'danger'
}

async function validateForm(formRef: FormInstance | undefined) {
  if (!formRef) {
    return false
  }

  return await formRef.validate().catch(() => false)
}

function openCreateLanguageModelDialog() {
  if (!canCreateLanguageModels.value) {
    return
  }

  configStore.openCreateLanguageModelDialog()
}

async function openCreateConversationTemplateDialog() {
  if (!canCreateConversationTemplates.value) {
    return
  }

  if (!hasLanguageModels.value) {
    ElMessage.warning('请先创建至少一个模型，再创建会话模板。')
    return
  }

  configStore.openCreateConversationTemplateDialog()
}

function openCreateApprovalPolicyDialog() {
  if (!canCreateApprovalPolicies.value) {
    return
  }

  configStore.openCreateApprovalPolicyDialog()
}

function openCreateBusinessDatabaseDialog() {
  if (!canCreateBusinessDatabases.value) {
    return
  }

  configStore.openCreateBusinessDatabaseDialog()
}

function openCreateMcpServerDialog() {
  if (!canCreateMcpServers.value) {
    return
  }

  configStore.openCreateMcpServerDialog()
}

async function openEditLanguageModelDialog(id: string) {
  if (!canUpdateLanguageModels.value) {
    return
  }

  try {
    await configStore.openEditLanguageModelDialog(id)
  } catch {
    // 错误已在 store 中展示
  }
}

async function openEditConversationTemplateDialog(id: string) {
  if (!canUpdateConversationTemplates.value) {
    return
  }

  if (!hasLanguageModels.value) {
    ElMessage.warning('当前没有可用模型，无法编辑会话模板。')
    return
  }

  try {
    await configStore.openEditConversationTemplateDialog(id)
  } catch {
    // 错误已在 store 中展示
  }
}

async function openEditApprovalPolicyDialog(id: string) {
  if (!canUpdateApprovalPolicies.value) {
    return
  }

  try {
    await configStore.openEditApprovalPolicyDialog(id)
  } catch {
    // 错误已在 store 中展示
  }
}

async function openEditBusinessDatabaseDialog(id: string) {
  if (!canUpdateBusinessDatabases.value) {
    return
  }

  try {
    await configStore.openEditBusinessDatabaseDialog(id)
  } catch {
    // 错误已在 store 中展示
  }
}

async function openEditMcpServerDialog(id: string) {
  if (!canUpdateMcpServers.value) {
    return
  }

  try {
    await configStore.openEditMcpServerDialog(id)
  } catch {
    // 错误已在 store 中展示
  }
}

function addApprovalTool() {
  const nextToolName = toolNameDraft.value.trim()
  if (!nextToolName) {
    return
  }

  if (!configStore.currentApprovalPolicy.toolNames.includes(nextToolName)) {
    configStore.currentApprovalPolicy.toolNames = [
      ...configStore.currentApprovalPolicy.toolNames,
      nextToolName
    ]
  }

  toolNameDraft.value = ''
}

function removeApprovalTool(toolName: string) {
  configStore.currentApprovalPolicy.toolNames = configStore.currentApprovalPolicy.toolNames.filter(
    (item) => item !== toolName
  )
}

function addMcpTool() {
  const nextToolName = mcpToolNameDraft.value.trim()
  if (!nextToolName) {
    return
  }

  if (
    !configStore.currentMcpServer.allowedToolNames.some(
      (item) => item.toLowerCase() === nextToolName.toLowerCase()
    )
  ) {
    configStore.currentMcpServer.allowedToolNames = [
      ...configStore.currentMcpServer.allowedToolNames,
      nextToolName
    ]
  }

  mcpToolNameDraft.value = ''
}

function removeMcpTool(toolName: string) {
  configStore.currentMcpServer.allowedToolNames =
    configStore.currentMcpServer.allowedToolNames.filter((item) => item !== toolName)
}

async function saveLanguageModel() {
  const valid = await validateForm(languageModelFormRef.value)
  if (!valid) {
    return
  }

  const mode = configStore.dialogModes.languageModel

  try {
    await configStore.saveLanguageModel()
    ElMessage.success(mode === 'create' ? '模型已创建。' : '模型已更新。')
  } catch {
    // 错误已在 store 中展示
  }
}

async function saveConversationTemplate() {
  const valid = await validateForm(conversationTemplateFormRef.value)
  if (!valid) {
    return
  }

  const mode = configStore.dialogModes.conversationTemplate

  try {
    await configStore.saveConversationTemplate()
    ElMessage.success(mode === 'create' ? '模板已创建。' : '模板已更新。')
  } catch {
    // 错误已在 store 中展示
  }
}

async function saveApprovalPolicy() {
  if (configStore.currentApprovalPolicy.toolNames.length === 0) {
    configStore.actionErrors.approvalPolicy = '请至少添加一个工具名称。'
    return
  }

  const valid = await validateForm(approvalPolicyFormRef.value)
  if (!valid) {
    return
  }

  const mode = configStore.dialogModes.approvalPolicy

  try {
    await configStore.saveApprovalPolicy()
    ElMessage.success(mode === 'create' ? '审批策略已创建。' : '审批策略已更新。')
  } catch {
    // 错误已在 store 中展示
  }
}

async function saveBusinessDatabase() {
  configStore.currentBusinessDatabase.isReadOnly = true

  const valid = await validateForm(businessDatabaseFormRef.value)
  if (!valid) {
    return
  }

  const mode = configStore.dialogModes.businessDatabase

  try {
    await configStore.saveBusinessDatabase()
    ElMessage.success(mode === 'create' ? '业务库已创建。' : '业务库已更新。')
  } catch {
    // 错误已在 store 中展示
  }
}

async function saveMcpServer() {
  const valid = await validateForm(mcpServerFormRef.value)
  if (!valid) {
    return
  }

  const mode = configStore.dialogModes.mcpServer

  try {
    await configStore.saveMcpServer()
    ElMessage.success(mode === 'create' ? 'MCP 服务已创建。' : 'MCP 服务已更新。')
  } catch {
    // 错误已在 store 中展示
  }
}

async function confirmDelete(
  title: string,
  message: string,
  action: () => Promise<void>,
  successMessage: string
) {
  try {
    await ElMessageBox.confirm(message, title, {
      type: 'warning',
      confirmButtonText: '确认删除',
      cancelButtonText: '取消'
    })
  } catch {
    return
  }

  try {
    await action()
    ElMessage.success(successMessage)
  } catch {
    // 错误已在 store 中展示
  }
}

async function deleteLanguageModel(id: string, name: string) {
  if (!canDeleteLanguageModels.value) {
    return
  }

  await confirmDelete(
    '删除模型',
    `确认删除模型“${name}”吗？`,
    () => configStore.deleteLanguageModel(id),
    '模型已删除。'
  )
}

async function deleteConversationTemplate(id: string, name: string) {
  if (!canDeleteConversationTemplates.value) {
    return
  }

  await confirmDelete(
    '删除模板',
    `确认删除模板“${name}”吗？`,
    () => configStore.deleteConversationTemplate(id),
    '模板已删除。'
  )
}

async function deleteApprovalPolicy(id: string, name: string) {
  if (!canDeleteApprovalPolicies.value) {
    return
  }

  await confirmDelete(
    '删除审批策略',
    `确认删除审批策略“${name}”吗？`,
    () => configStore.deleteApprovalPolicy(id),
    '审批策略已删除。'
  )
}

async function deleteBusinessDatabase(id: string, name: string) {
  if (!canDeleteBusinessDatabases.value) {
    return
  }

  await confirmDelete(
    '删除业务库',
    `确认删除业务库“${name}”吗？`,
    () => configStore.deleteBusinessDatabase(id),
    '业务库已删除。'
  )
}

async function deleteMcpServer(id: string, name: string) {
  if (!canDeleteMcpServers.value) {
    return
  }

  await confirmDelete(
    '删除 MCP 服务',
    `确认删除 MCP 服务“${name}”吗？`,
    () => configStore.deleteMcpServer(id),
    'MCP 服务已删除。'
  )
}

onMounted(async () => {
  try {
    await configStore.refresh()
  } catch {
    // 顶部错误提示已展示
  }
})
</script>

<template>
  <AppShell>
    <div class="config-page">
      <section class="hero">
        <div>
          <h1>配置管理台</h1>
          <p>统一管理模型、模板、审批策略和业务库，语义源状态继续作为只读运维视图展示。</p>
        </div>
        <div class="hero-metrics">
          <el-statistic title="语义源就绪数" :value="readySemanticSources" />
          <el-statistic title="业务库总数" :value="configStore.businessDatabases.length" />
        </div>
      </section>

      <el-alert
        v-if="configStore.errorMessage"
        :title="configStore.errorMessage"
        type="error"
        show-icon
        :closable="false"
      />

      <div v-if="configStore.isLoading" class="loading-panel">
        <el-skeleton :rows="8" animated />
      </div>

      <template v-else>
        <section class="section-grid">
          <el-card v-if="canReadLanguageModels" class="section-card" shadow="hover">
            <template #header>
              <div class="section-header">
                <div class="section-title">
                  <span>模型配置</span>
                  <el-tag type="info">{{ configStore.languageModels.length }}</el-tag>
                </div>
                <el-button
                  v-if="canCreateLanguageModels"
                  type="primary"
                  plain
                  size="small"
                  @click="openCreateLanguageModelDialog"
                >
                  新增模型
                </el-button>
              </div>
            </template>

            <el-alert
              v-if="configStore.actionErrors.languageModel"
              :title="configStore.actionErrors.languageModel"
              type="error"
              show-icon
              :closable="false"
              class="section-alert"
            />

            <el-table :data="configStore.languageModels" size="small" empty-text="暂无模型配置">
              <el-table-column prop="provider" label="提供方" min-width="120" />
              <el-table-column prop="name" label="名称" min-width="180" />
              <el-table-column prop="baseUrl" label="地址" min-width="220" />
              <el-table-column prop="maxTokens" label="最大令牌数" width="120" />
              <el-table-column prop="temperature" label="温度" width="100" />
              <el-table-column label="密钥" width="110">
                <template #default="{ row }">
                  <el-tag :type="row.hasApiKey ? 'success' : 'info'">
                    {{ row.hasApiKey ? '已配置' : '未配置' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="160" fixed="right">
                <template #default="{ row }">
                  <div class="table-actions">
                    <el-button
                      v-if="canUpdateLanguageModels"
                      link
                      type="primary"
                      @click="openEditLanguageModelDialog(row.id)"
                    >
                      编辑
                    </el-button>
                    <el-button
                      v-if="canDeleteLanguageModels"
                      link
                      type="danger"
                      @click="deleteLanguageModel(row.id, row.name)"
                    >
                      删除
                    </el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>
          </el-card>

          <el-card v-if="canReadConversationTemplates" class="section-card" shadow="hover">
            <template #header>
              <div class="section-header">
                <div class="section-title">
                  <span>会话模板</span>
                  <el-tag type="info">{{ configStore.conversationTemplates.length }}</el-tag>
                </div>
                <el-button
                  v-if="canCreateConversationTemplates"
                  type="primary"
                  plain
                  size="small"
                  @click="openCreateConversationTemplateDialog"
                >
                  新增模板
                </el-button>
              </div>
            </template>

            <el-alert
              v-if="configStore.actionErrors.conversationTemplate"
              :title="configStore.actionErrors.conversationTemplate"
              type="error"
              show-icon
              :closable="false"
              class="section-alert"
            />

            <el-table :data="configStore.conversationTemplates" size="small" empty-text="暂无模板配置">
              <el-table-column prop="name" label="名称" min-width="180" />
              <el-table-column prop="description" label="说明" min-width="220" />
              <el-table-column label="启用状态" width="100">
                <template #default="{ row }">
                  <el-tag :type="enabledTagType(row.isEnabled)">
                    {{ row.isEnabled ? '启用' : '停用' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="160" fixed="right">
                <template #default="{ row }">
                  <div class="table-actions">
                    <el-button
                      v-if="canUpdateConversationTemplates"
                      link
                      type="primary"
                      @click="openEditConversationTemplateDialog(row.id)"
                    >
                      编辑
                    </el-button>
                    <el-button
                      v-if="canDeleteConversationTemplates"
                      link
                      type="danger"
                      @click="deleteConversationTemplate(row.id, row.name)"
                    >
                      删除
                    </el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>
          </el-card>
        </section>

        <section class="section-grid">
          <el-card v-if="canReadApprovalPolicies" class="section-card" shadow="hover">
            <template #header>
              <div class="section-header">
                <div class="section-title">
                  <span>审批策略</span>
                  <el-tag type="info">{{ configStore.approvalPolicies.length }}</el-tag>
                </div>
                <el-button
                  v-if="canCreateApprovalPolicies"
                  type="primary"
                  plain
                  size="small"
                  @click="openCreateApprovalPolicyDialog"
                >
                  新增策略
                </el-button>
              </div>
            </template>

            <el-alert
              v-if="configStore.actionErrors.approvalPolicy"
              :title="configStore.actionErrors.approvalPolicy"
              type="error"
              show-icon
              :closable="false"
              class="section-alert"
            />

            <el-table :data="configStore.approvalPolicies" size="small" empty-text="暂无审批策略">
              <el-table-column prop="name" label="名称" min-width="180" />
              <el-table-column prop="targetType" label="目标类型" width="120" />
              <el-table-column prop="targetName" label="目标名称" min-width="180" />
              <el-table-column label="工具" min-width="220">
                <template #default="{ row }">
                  <div class="chip-list">
                    <el-tag v-for="toolName in row.toolNames" :key="toolName" size="small">
                      {{ toolName }}
                    </el-tag>
                  </div>
                </template>
              </el-table-column>
              <el-table-column label="启用状态" width="100">
                <template #default="{ row }">
                  <el-tag :type="enabledTagType(row.isEnabled)">
                    {{ row.isEnabled ? '启用' : '停用' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="160" fixed="right">
                <template #default="{ row }">
                  <div class="table-actions">
                    <el-button
                      v-if="canUpdateApprovalPolicies"
                      link
                      type="primary"
                      @click="openEditApprovalPolicyDialog(row.id)"
                    >
                      编辑
                    </el-button>
                    <el-button
                      v-if="canDeleteApprovalPolicies"
                      link
                      type="danger"
                      @click="deleteApprovalPolicy(row.id, row.name)"
                    >
                      删除
                    </el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>
          </el-card>

          <el-card v-if="canReadBusinessDatabases" class="section-card" shadow="hover">
            <template #header>
              <div class="section-header">
                <div class="section-title">
                  <span>业务库</span>
                  <el-tag type="info">{{ configStore.businessDatabases.length }}</el-tag>
                </div>
                <el-button
                  v-if="canCreateBusinessDatabases"
                  type="primary"
                  plain
                  size="small"
                  @click="openCreateBusinessDatabaseDialog"
                >
                  新增业务库
                </el-button>
              </div>
            </template>

            <el-alert
              v-if="configStore.actionErrors.businessDatabase"
              :title="configStore.actionErrors.businessDatabase"
              type="error"
              show-icon
              :closable="false"
              class="section-alert"
            />

            <el-alert
              title="查询链路会区分业务库停用、非只读配置、SQL 安全拒绝和结果截断；配置管理台保存时始终强制只读。"
              type="info"
              show-icon
              :closable="false"
              class="section-alert"
            />

            <el-table :data="configStore.businessDatabases" size="small" empty-text="暂无业务库配置">
              <el-table-column prop="name" label="名称" min-width="160" />
              <el-table-column label="数据库类型" width="120">
                <template #default="{ row }">
                  {{ providerLabel(row.provider) }}
                </template>
              </el-table-column>
              <el-table-column label="启用状态" width="100">
                <template #default="{ row }">
                  <el-tag :type="enabledTagType(row.isEnabled)">
                    {{ row.isEnabled ? '启用' : '停用' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="只读状态" width="100">
                <template #default="{ row }">
                  <el-tag :type="readonlyTagType(row.isReadOnly)">
                    {{ row.isReadOnly ? '只读' : '可写' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="连接串" width="110">
                <template #default="{ row }">
                  <el-tag :type="row.hasConnectionString ? 'success' : 'warning'">
                    {{ row.hasConnectionString ? '已配置' : '未配置' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="160" fixed="right">
                <template #default="{ row }">
                  <div class="table-actions">
                    <el-button
                      v-if="canUpdateBusinessDatabases"
                      link
                      type="primary"
                      @click="openEditBusinessDatabaseDialog(row.id)"
                    >
                      编辑
                    </el-button>
                    <el-button
                      v-if="canDeleteBusinessDatabases"
                      link
                      type="danger"
                      @click="deleteBusinessDatabase(row.id, row.name)"
                    >
                      删除
                    </el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>
          </el-card>

          <el-card v-if="canReadMcpServers" class="section-card" shadow="hover">
            <template #header>
              <div class="section-header">
                <div class="section-title">
                  <span>MCP 服务</span>
                  <el-tag type="info">{{ configStore.mcpServers.length }}</el-tag>
                </div>
                <el-button
                  v-if="canCreateMcpServers"
                  type="primary"
                  plain
                  size="small"
                  @click="openCreateMcpServerDialog"
                >
                  新增 MCP
                </el-button>
              </div>
            </template>

            <el-alert
              title="MCP 配置由启动期 bootstrap 读取；启用、禁用、工具暴露或连接参数变更后，需要重启服务才会影响运行时。"
              type="warning"
              show-icon
              :closable="false"
              class="section-alert"
            />

            <el-alert
              v-if="configStore.actionErrors.mcpServer"
              :title="configStore.actionErrors.mcpServer"
              type="error"
              show-icon
              :closable="false"
              class="section-alert"
            />

            <el-table :data="configStore.mcpServers" size="small" empty-text="暂无 MCP 服务">
              <el-table-column prop="name" label="名称" min-width="160" />
              <el-table-column label="传输" width="100">
                <template #default="{ row }">
                  {{ mcpTransportLabel(row.transportType) }}
                </template>
              </el-table-column>
              <el-table-column label="暴露模式" width="110">
                <template #default="{ row }">
                  <el-tag :type="chatExposureTagType(row.chatExposureMode)">
                    {{ chatExposureLabel(row.chatExposureMode) }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="启用状态" width="100">
                <template #default="{ row }">
                  <el-tag :type="enabledTagType(row.isEnabled)">
                    {{ row.isEnabled ? '启用' : '停用' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="参数" width="100">
                <template #default="{ row }">
                  <el-tag :type="row.hasArguments ? 'success' : 'warning'">
                    {{ row.hasArguments ? '已配置' : '未配置' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="允许工具" min-width="220">
                <template #default="{ row }">
                  <div v-if="row.allowedToolNames.length > 0" class="chip-list">
                    <el-tag v-for="toolName in row.allowedToolNames" :key="toolName" size="small">
                      {{ toolName }}
                    </el-tag>
                  </div>
                  <span v-else class="field-tip">未开放</span>
                </template>
              </el-table-column>
              <el-table-column label="审批" min-width="220">
                <template #default="{ row }">
                  <div v-if="row.toolPolicySummaries.length > 0" class="chip-list">
                    <el-tag
                      v-for="policy in row.toolPolicySummaries"
                      :key="`${row.id}-${policy.toolName}`"
                      :type="policy.requiresApproval ? 'warning' : 'info'"
                      size="small"
                    >
                      {{ policy.toolName }}{{ policy.requiresOnsiteAttestation ? ' / 在岗' : '' }}
                    </el-tag>
                  </div>
                  <span v-else class="field-tip">未绑定审批策略</span>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="160" fixed="right">
                <template #default="{ row }">
                  <div class="table-actions">
                    <el-button
                      v-if="canUpdateMcpServers"
                      link
                      type="primary"
                      @click="openEditMcpServerDialog(row.id)"
                    >
                      编辑
                    </el-button>
                    <el-button
                      v-if="canDeleteMcpServers"
                      link
                      type="danger"
                      @click="deleteMcpServer(row.id, row.name)"
                    >
                      删除
                    </el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>
          </el-card>
        </section>

        <el-card v-if="canReadSemanticSources" class="section-card" shadow="hover">
          <template #header>
            <div class="section-header">
              <div class="section-title">
                <span>语义数据源状态</span>
                <el-tag type="info">{{ configStore.semanticSourceStatuses.length }}</el-tag>
              </div>
            </div>
          </template>

          <el-alert
            title="真实源切换前先看这里：只有五个 target 全部 Ready，才允许把 cloud-sim 换成真实只读源。"
            type="info"
            show-icon
            :closable="false"
            class="section-alert"
          />

          <el-table :data="configStore.semanticSourceStatuses" size="small" empty-text="暂无语义源状态">
            <el-table-column prop="target" label="业务域" width="150" />
            <el-table-column prop="databaseName" label="绑定数据库" min-width="180" />
            <el-table-column prop="effectiveSourceName" label="最终识别源" min-width="240" />
            <el-table-column label="启用状态" width="100">
              <template #default="{ row }">
                <el-tag :type="enabledTagType(row.isEnabled)">
                  {{ row.isEnabled ? '启用' : '停用' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="只读状态" width="100">
              <template #default="{ row }">
                <el-tag :type="readonlyTagType(row.isReadOnly)">
                  {{ row.isReadOnly ? '只读' : '可写' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="状态" width="140">
              <template #default="{ row }">
                <el-tag :type="semanticStatusType(row.status)">
                  {{ row.status }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="Provider" width="120">
              <template #default="{ row }">
                <el-tag
                  :type="
                    semanticValidationTagType(
                      semanticProviderMatchedState(row.status, row.providerMatched)
                    )
                  "
                >
                  {{
                    semanticValidationText(
                      semanticProviderMatchedState(row.status, row.providerMatched),
                      '已对齐',
                      '未对齐'
                    )
                  }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="源存在" width="120">
              <template #default="{ row }">
                <el-tag
                  :type="
                    semanticValidationTagType(
                      semanticSourceExistsState(row.status, row.sourceExists)
                    )
                  "
                >
                  {{
                    semanticValidationText(
                      semanticSourceExistsState(row.status, row.sourceExists),
                      '已可查',
                      '不存在'
                    )
                  }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="缺失字段" min-width="220">
              <template #default="{ row }">
                <span v-if="row.missingRequiredFields.length === 0">无</span>
                <div v-else class="chip-list">
                  <el-tag
                    v-for="field in row.missingRequiredFields"
                    :key="`${row.target}-${field}`"
                    type="danger"
                    size="small"
                  >
                    {{ field }}
                  </el-tag>
                </div>
              </template>
            </el-table-column>
            <el-table-column label="说明" min-width="320">
              <template #default="{ row }">
                <div class="semantic-status-detail">
                  <div>{{ semanticStatusDescription(row) }}</div>
                  <div v-if="row.sourceName && row.sourceName !== row.effectiveSourceName" class="field-tip">
                    SourceName: {{ row.sourceName }}
                  </div>
                </div>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </template>
    </div>

    <el-dialog
      v-model="configStore.dialogStates.languageModel"
      :title="configStore.dialogModes.languageModel === 'create' ? '新增模型' : '编辑模型'"
      width="620px"
      destroy-on-close
      @closed="configStore.closeLanguageModelDialog()"
    >
      <el-alert
        v-if="configStore.actionErrors.languageModel"
        :title="configStore.actionErrors.languageModel"
        type="error"
        show-icon
        :closable="false"
        class="dialog-alert"
      />

      <el-form
        ref="languageModelFormRef"
        :model="configStore.currentLanguageModel"
        :rules="languageModelRules"
        label-position="top"
      >
        <el-form-item label="提供方" prop="provider">
          <el-input v-model="configStore.currentLanguageModel.provider" placeholder="例如 OpenAI" />
        </el-form-item>
        <el-form-item label="模型名称" prop="name">
          <el-input v-model="configStore.currentLanguageModel.name" placeholder="请输入模型名称" />
        </el-form-item>
        <el-form-item label="模型地址" prop="baseUrl">
          <el-input v-model="configStore.currentLanguageModel.baseUrl" placeholder="请输入模型地址" />
        </el-form-item>
        <el-form-item label="API Key">
          <el-radio-group
            v-if="isLanguageModelSecretActionEditable"
            v-model="configStore.currentLanguageModel.apiKeyAction"
            class="secret-mode-group"
          >
            <el-radio-button label="keep">保留现有密钥</el-radio-button>
            <el-radio-button label="replace">替换为新密钥</el-radio-button>
            <el-radio-button label="clear">清空密钥</el-radio-button>
          </el-radio-group>
          <el-input
            v-model="configStore.currentLanguageModel.apiKey"
            type="password"
            show-password
            :disabled="!isLanguageModelApiKeyInputEnabled"
            :placeholder="languageModelApiKeyPlaceholder"
          />
          <div v-if="configStore.dialogModes.languageModel === 'edit'" class="field-tip">
            当前密钥状态：{{ configStore.currentLanguageModel.hasApiKey ? '已配置' : '未配置' }}
          </div>
          <div
            v-if="
              configStore.dialogModes.languageModel === 'edit' &&
              configStore.currentLanguageModel.apiKeyAction === 'clear'
            "
            class="field-tip"
          >
            保存后将立即清空当前模型的 API Key。
          </div>
        </el-form-item>
        <div class="inline-fields">
          <el-form-item label="最大令牌数" prop="maxTokens">
            <el-input-number
              v-model="configStore.currentLanguageModel.maxTokens"
              :min="1"
              :step="256"
              controls-position="right"
            />
          </el-form-item>
          <el-form-item label="温度" prop="temperature">
            <el-input-number
              v-model="configStore.currentLanguageModel.temperature"
              :min="0"
              :max="2"
              :step="0.1"
              controls-position="right"
            />
          </el-form-item>
        </div>
      </el-form>

      <template #footer>
        <div class="dialog-actions">
          <el-button @click="configStore.closeLanguageModelDialog()">取消</el-button>
          <el-button type="primary" :loading="configStore.submittingStates.languageModel" @click="saveLanguageModel">
            保存
          </el-button>
        </div>
      </template>
    </el-dialog>

    <el-dialog
      v-model="configStore.dialogStates.conversationTemplate"
      :title="configStore.dialogModes.conversationTemplate === 'create' ? '新增模板' : '编辑模板'"
      width="720px"
      destroy-on-close
      @closed="configStore.closeConversationTemplateDialog()"
    >
      <el-alert
        v-if="configStore.actionErrors.conversationTemplate"
        :title="configStore.actionErrors.conversationTemplate"
        type="error"
        show-icon
        :closable="false"
        class="dialog-alert"
      />

      <el-form
        ref="conversationTemplateFormRef"
        :model="configStore.currentConversationTemplate"
        :rules="conversationTemplateRules"
        label-position="top"
      >
        <el-form-item label="模板名称" prop="name">
          <el-input v-model="configStore.currentConversationTemplate.name" placeholder="请输入模板名称" />
        </el-form-item>
        <el-form-item label="模板说明" prop="description">
          <el-input v-model="configStore.currentConversationTemplate.description" placeholder="请输入模板说明" />
        </el-form-item>
        <el-form-item label="系统提示词" prop="systemPrompt">
          <el-input
            v-model="configStore.currentConversationTemplate.systemPrompt"
            type="textarea"
            :rows="8"
            placeholder="请输入系统提示词"
          />
        </el-form-item>
        <el-form-item label="关联模型" prop="modelId">
          <el-select
            v-model="configStore.currentConversationTemplate.modelId"
            placeholder="请选择关联模型"
            style="width: 100%"
          >
            <el-option
              v-for="item in configStore.languageModels"
              :key="item.id"
              :label="`${item.name} (${item.provider})`"
              :value="item.id"
            />
          </el-select>
        </el-form-item>
        <div class="inline-fields">
          <el-form-item label="最大令牌数">
            <div class="optional-field">
              <el-input-number
                v-model="configStore.currentConversationTemplate.maxTokens"
                :min="1"
                :step="256"
                controls-position="right"
              />
              <el-button text @click="configStore.currentConversationTemplate.maxTokens = null">
                清空
              </el-button>
            </div>
          </el-form-item>
          <el-form-item label="温度">
            <div class="optional-field">
              <el-input-number
                v-model="configStore.currentConversationTemplate.temperature"
                :min="0"
                :max="2"
                :step="0.1"
                controls-position="right"
              />
              <el-button text @click="configStore.currentConversationTemplate.temperature = null">
                清空
              </el-button>
            </div>
          </el-form-item>
        </div>
        <el-form-item label="启用状态">
          <el-switch v-model="configStore.currentConversationTemplate.isEnabled" />
        </el-form-item>
      </el-form>

      <template #footer>
        <div class="dialog-actions">
          <el-button @click="configStore.closeConversationTemplateDialog()">取消</el-button>
          <el-button
            type="primary"
            :loading="configStore.submittingStates.conversationTemplate"
            @click="saveConversationTemplate"
          >
            保存
          </el-button>
        </div>
      </template>
    </el-dialog>

    <el-dialog
      v-model="configStore.dialogStates.approvalPolicy"
      :title="configStore.dialogModes.approvalPolicy === 'create' ? '新增审批策略' : '编辑审批策略'"
      width="680px"
      destroy-on-close
      @closed="configStore.closeApprovalPolicyDialog()"
    >
      <el-alert
        v-if="configStore.actionErrors.approvalPolicy"
        :title="configStore.actionErrors.approvalPolicy"
        type="error"
        show-icon
        :closable="false"
        class="dialog-alert"
      />

      <el-form
        ref="approvalPolicyFormRef"
        :model="configStore.currentApprovalPolicy"
        :rules="approvalPolicyRules"
        label-position="top"
      >
        <el-form-item label="策略名称" prop="name">
          <el-input v-model="configStore.currentApprovalPolicy.name" placeholder="请输入策略名称" />
        </el-form-item>
        <el-form-item label="策略说明">
          <el-input v-model="configStore.currentApprovalPolicy.description" placeholder="请输入策略说明" />
        </el-form-item>
        <div class="inline-fields">
          <el-form-item label="目标类型" prop="targetType">
            <el-select v-model="configStore.currentApprovalPolicy.targetType" style="width: 100%">
              <el-option
                v-for="item in approvalTargetOptions"
                :key="item.value"
                :label="item.label"
                :value="item.value"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="目标名称" prop="targetName">
            <el-input v-model="configStore.currentApprovalPolicy.targetName" placeholder="请输入目标名称" />
          </el-form-item>
        </div>
        <el-form-item label="工具名称">
          <div class="tool-editor">
            <div class="chip-list">
              <el-tag
                v-for="toolName in configStore.currentApprovalPolicy.toolNames"
                :key="toolName"
                closable
                @close="removeApprovalTool(toolName)"
              >
                {{ toolName }}
              </el-tag>
            </div>
            <div class="tool-input-row">
              <el-input
                v-model="toolNameDraft"
                placeholder="输入工具名称后回车"
                @keyup.enter="addApprovalTool"
              />
              <el-button @click="addApprovalTool">添加</el-button>
            </div>
          </div>
        </el-form-item>
        <el-form-item label="审批前置条件">
          <el-switch v-model="configStore.currentApprovalPolicy.requiresOnsiteAttestation" />
          <div class="field-tip">
            开启后，批准该策略下的工具前必须先有有效的会话级在岗声明，并在审批时再次确认现场有人在岗。
          </div>
        </el-form-item>
        <el-form-item label="启用状态">
          <el-switch v-model="configStore.currentApprovalPolicy.isEnabled" />
        </el-form-item>
      </el-form>

      <template #footer>
        <div class="dialog-actions">
          <el-button @click="configStore.closeApprovalPolicyDialog()">取消</el-button>
          <el-button type="primary" :loading="configStore.submittingStates.approvalPolicy" @click="saveApprovalPolicy">
            保存
          </el-button>
        </div>
      </template>
    </el-dialog>

    <el-dialog
      v-model="configStore.dialogStates.businessDatabase"
      :title="configStore.dialogModes.businessDatabase === 'create' ? '新增业务库' : '编辑业务库'"
      width="680px"
      destroy-on-close
      @closed="configStore.closeBusinessDatabaseDialog()"
    >
      <el-alert
        v-if="configStore.actionErrors.businessDatabase"
        :title="configStore.actionErrors.businessDatabase"
        type="error"
        show-icon
        :closable="false"
        class="dialog-alert"
      />

      <el-form
        ref="businessDatabaseFormRef"
        :model="configStore.currentBusinessDatabase"
        :rules="businessDatabaseRules"
        label-position="top"
      >
        <el-form-item label="业务库名称" prop="name">
          <el-input v-model="configStore.currentBusinessDatabase.name" placeholder="请输入业务库名称" />
        </el-form-item>
        <el-form-item label="业务库说明" prop="description">
          <el-input
            v-model="configStore.currentBusinessDatabase.description"
            type="textarea"
            :rows="3"
            placeholder="请输入业务库说明"
          />
        </el-form-item>
        <div class="inline-fields">
          <el-form-item label="数据库类型" prop="provider">
            <el-select
              v-model="configStore.currentBusinessDatabase.provider"
              style="width: 100%"
              :disabled="isBusinessDatabaseProviderLocked"
            >
              <el-option
                v-for="item in databaseProviderOptions"
                :key="item.value"
                :label="item.label"
                :value="item.value"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="启用状态">
            <el-switch v-model="configStore.currentBusinessDatabase.isEnabled" />
          </el-form-item>
        </div>
        <el-form-item label="连接字符串" prop="connectionString">
          <el-input
            v-model="configStore.currentBusinessDatabase.connectionString"
            type="password"
            show-password
            placeholder="新增时必填；编辑时留空表示保持原值"
          />
          <div class="field-tip">
            当前连接信息：{{ configStore.currentBusinessDatabase.hasConnectionString ? '已配置' : '未配置' }}
          </div>
        </el-form-item>
        <el-form-item label="只读状态">
          <el-switch :model-value="true" disabled />
          <div class="field-tip">配置管理台只允许保存为只读业务库。</div>
        </el-form-item>
      </el-form>

      <template #footer>
        <div class="dialog-actions">
          <el-button @click="configStore.closeBusinessDatabaseDialog()">取消</el-button>
          <el-button type="primary" :loading="configStore.submittingStates.businessDatabase" @click="saveBusinessDatabase">
            保存
          </el-button>
        </div>
      </template>
    </el-dialog>

    <el-dialog
      v-model="configStore.dialogStates.mcpServer"
      :title="configStore.dialogModes.mcpServer === 'create' ? '新增 MCP 服务' : '编辑 MCP 服务'"
      width="720px"
      destroy-on-close
      @closed="configStore.closeMcpServerDialog()"
    >
      <el-alert
        title="运行时只在服务启动时加载 MCP 配置；保存后需重启服务才会影响工具暴露。"
        type="warning"
        show-icon
        :closable="false"
        class="dialog-alert"
      />

      <el-alert
        v-if="configStore.actionErrors.mcpServer"
        :title="configStore.actionErrors.mcpServer"
        type="error"
        show-icon
        :closable="false"
        class="dialog-alert"
      />

      <el-form
        ref="mcpServerFormRef"
        :model="configStore.currentMcpServer"
        :rules="mcpServerRules"
        label-position="top"
      >
        <el-form-item label="服务名称" prop="name">
          <el-input v-model="configStore.currentMcpServer.name" placeholder="请输入 MCP 服务名称" />
        </el-form-item>
        <el-form-item label="服务说明" prop="description">
          <el-input
            v-model="configStore.currentMcpServer.description"
            type="textarea"
            :rows="3"
            placeholder="请输入 MCP 服务说明"
          />
        </el-form-item>
        <div class="inline-fields">
          <el-form-item label="传输类型" prop="transportType">
            <el-select v-model="configStore.currentMcpServer.transportType" style="width: 100%">
              <el-option
                v-for="item in mcpTransportOptions"
                :key="item.value"
                :label="item.label"
                :value="item.value"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="启用状态">
            <el-switch v-model="configStore.currentMcpServer.isEnabled" />
          </el-form-item>
        </div>
        <el-form-item
          v-if="configStore.currentMcpServer.transportType === 1"
          label="启动命令"
          prop="command"
        >
          <el-input v-model="configStore.currentMcpServer.command" placeholder="例如 dotnet、node、python" />
        </el-form-item>
        <el-form-item
          :label="configStore.currentMcpServer.transportType === 2 ? 'SSE 地址' : '启动参数'"
          prop="arguments"
        >
          <el-input
            v-model="configStore.currentMcpServer.arguments"
            type="textarea"
            :rows="3"
            :placeholder="
              configStore.dialogModes.mcpServer === 'create'
                ? '请输入启动参数或 SSE 地址'
                : '留空表示保留已保存参数'
            "
          />
          <div class="field-tip">
            当前参数状态：{{ configStore.currentMcpServer.hasArguments ? '已配置' : '未配置' }}。详情不会从后端回传。
          </div>
        </el-form-item>
        <div class="inline-fields">
          <el-form-item label="聊天暴露模式">
            <el-select v-model="configStore.currentMcpServer.chatExposureMode" style="width: 100%">
              <el-option
                v-for="item in chatExposureOptions"
                :key="item.value"
                :label="item.label"
                :value="item.value"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="运行生效">
            <el-tag type="warning">重启生效</el-tag>
            <div class="field-tip">禁用、暴露模式和 allowlist 都不会热卸载已启动工具。</div>
          </el-form-item>
        </div>
        <el-form-item label="允许工具">
          <div class="tool-editor">
            <div class="chip-list">
              <el-tag
                v-for="toolName in configStore.currentMcpServer.allowedToolNames"
                :key="toolName"
                closable
                @close="removeMcpTool(toolName)"
              >
                {{ toolName }}
              </el-tag>
            </div>
            <div class="tool-input-row">
              <el-input
                v-model="mcpToolNameDraft"
                placeholder="输入 MCP 工具名后回车"
                @keyup.enter="addMcpTool"
              />
              <el-button @click="addMcpTool">添加</el-button>
            </div>
            <div class="field-tip">只有 allowlist 中的工具才会暴露给聊天链路。</div>
          </div>
        </el-form-item>
      </el-form>

      <template #footer>
        <div class="dialog-actions">
          <el-button @click="configStore.closeMcpServerDialog()">取消</el-button>
          <el-button type="primary" :loading="configStore.submittingStates.mcpServer" @click="saveMcpServer">
            保存
          </el-button>
        </div>
      </template>
    </el-dialog>
  </AppShell>
</template>

<style scoped>
.config-page {
  display: grid;
  gap: 18px;
}

.hero {
  display: flex;
  justify-content: space-between;
  gap: 18px;
  padding: 20px 24px;
  border-radius: 24px;
  background: rgba(255, 255, 255, 0.9);
  box-shadow: 0 10px 32px rgba(15, 23, 42, 0.08);
}

.hero h1 {
  font-size: 24px;
  font-weight: 700;
  color: #0f172a;
}

.hero p {
  margin-top: 8px;
  color: #64748b;
}

.hero-metrics {
  display: flex;
  gap: 20px;
}

.loading-panel {
  padding: 24px;
  border-radius: 24px;
  background: rgba(255, 255, 255, 0.88);
}

.section-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 18px;
}

.section-card {
  border-radius: 20px;
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.section-title {
  display: flex;
  align-items: center;
  gap: 10px;
}

.section-alert,
.dialog-alert {
  margin-bottom: 16px;
}

.table-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.chip-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.inline-fields {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px;
}

.optional-field {
  display: flex;
  align-items: center;
  gap: 10px;
}

.tool-editor {
  display: grid;
  gap: 10px;
  width: 100%;
}

.tool-input-row {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 10px;
}

.secret-mode-group {
  margin-bottom: 10px;
}

.field-tip {
  margin-top: 8px;
  font-size: 12px;
  color: #64748b;
}

.semantic-status-detail {
  display: grid;
  gap: 6px;
}

.dialog-actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
}

@media (max-width: 1080px) {
  .section-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 768px) {
  .hero {
    flex-direction: column;
  }

  .hero-metrics {
    justify-content: space-between;
  }

  .inline-fields,
  .tool-input-row {
    grid-template-columns: 1fr;
  }
}
</style>
