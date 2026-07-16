<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { Bot, ClipboardList, Network, RefreshCw, Sparkles } from 'lucide-vue-next'
import AiButton from '@/components/ai/AiButton.vue'
import AiCard from '@/components/ai/AiCard.vue'
import AiCheckbox from '@/components/ai/AiCheckbox.vue'
import AiDataPage from '@/components/ai/AiDataPage.vue'
import AiDrawer from '@/components/ai/AiDrawer.vue'
import AiInput from '@/components/ai/AiInput.vue'
import AiNumberInput from '@/components/ai/AiNumberInput.vue'
import AiSelect from '@/components/ai/AiSelect.vue'
import AiSwitch from '@/components/ai/AiSwitch.vue'
import AiTag from '@/components/ai/AiTag.vue'
import AiTextarea from '@/components/ai/AiTextarea.vue'
import AppShell from '@/components/layout/AppShell.vue'
import { CONFIG_STORE_MESSAGES } from '@/constants/messages'
import { showAiToast } from '@/composables/useAiFeedback'
import { configService } from '@/services/configService'
import { useConfigStore } from '@/stores/configStore'
import { toStoreErrorMessage } from '@/stores/useDialogCrud'
import { getSkillDisplayDescription } from '@/utils/skillDisplay'
import type {
  CloudReadonlyStatus,
  ConversationTemplateSummary,
  LanguageModelSummary,
  LanguageModelTestResult,
  LanguageModelUsage,
  AgentPlannerToolSummary,
  SkillDefinition
} from '@/types/app'

type AgentSlotKey = 'intent' | 'planner' | 'executor'

type AgentSlotDefinition = {
  key: AgentSlotKey
  title: string
  subtitle: string
  modelUsage: LanguageModelUsage
  icon: typeof Network
  tone: 'blue' | 'teal' | 'violet'
  templateCode: string
  templateScope: string
  defaultModelName: string
}

const store = useConfigStore()

const slotDefinitions: AgentSlotDefinition[] = [
  {
    key: 'intent',
    title: '意图识别',
    subtitle: '只选择技能和意图，不回答、不计划、不执行。',
    modelUsage: 'Routing',
    icon: Network,
    tone: 'blue',
    templateCode: 'IntentRoutingAgent',
    templateScope: 'IntentRouting',
    defaultModelName: 'Intent Routing Model'
  },
  {
    key: 'planner',
    title: '计划生成',
    subtitle: '只根据目标生成可确认计划，不调用工具、不写文件。',
    modelUsage: 'Planner',
    icon: ClipboardList,
    tone: 'teal',
    templateCode: 'agent_planner',
    templateScope: 'AgentPlanner',
    defaultModelName: 'Plan Generator Model'
  },
  {
    key: 'executor',
    title: '最终执行',
    subtitle: '只执行已确认计划，只使用授权能力，结果写入受控工作区。',
    modelUsage: 'Chat',
    icon: Bot,
    tone: 'violet',
    templateCode: 'agent_executor',
    templateScope: 'AgentExecutor',
    defaultModelName: 'Executor Model'
  }
]

const protocolOptions = [
  { label: 'OpenAI-compatible', value: 'OpenAICompatible' },
  { label: 'Claude / Anthropic Messages', value: 'AnthropicMessages' }
]

const protocolLabels: Record<string, string> = {
  OpenAICompatible: 'OpenAI-compatible',
  AnthropicMessages: 'Claude / Anthropic Messages'
}

const usageLabels: Record<LanguageModelUsage, string> = {
  Chat: '对话/执行',
  Routing: '意图识别',
  Planner: '计划生成',
  Embedding: '向量'
}

const templateScopeLabels: Record<string, string> = {
  IntentRouting: '意图识别',
  AgentPlanner: '计划生成',
  AgentExecutor: '最终执行'
}

const dataSourceModeLabels: Record<string, string> = {
  CloudReadOnly: 'Cloud 只读',
  CloudReadonly: 'Cloud 只读',
  SimulationBusiness: 'AI 模拟业务库',
  BusinessDatabase: '只读业务库',
  Workspace: '工作区文件',
  UploadedFile: '上传文件',
  FreeGoal: '自由目标'
}

const knowledgeScopeLabels: Record<string, string> = {
  SelectedKnowledgeBase: '已选知识库',
  AllVisibleKnowledgeBases: '可见知识库',
  None: '不使用知识库'
}

const approvalPolicyLabels: Record<string, string> = {
  ToolApproval: '工具执行需确认',
  FinalOutputApproval: '最终输出需确认',
  PlanApproval: '计划需确认',
  None: '无需审批'
}

const outputComponentLabels: Record<string, string> = {
  chart: '图表',
  html: 'HTML',
  markdown: 'Markdown',
  pdf: 'PDF',
  pptx: 'PPTX',
  xlsx: 'XLSX',
  text: '文本'
}

const connectivityLabels: Record<string, string> = {
  Unknown: '未测试',
  Succeeded: '配置完成',
  Failed: '异常'
}

const testingModelIds = ref<Set<string>>(new Set())
const drawerTesting = ref(false)
const skillDefinitions = ref<SkillDefinition[]>([])
const toolSummaries = ref<AgentPlannerToolSummary[]>([])
const isLoadingSkills = ref(false)
const isLoadingToolCatalog = ref(false)
const toolCatalogLoaded = ref(false)

function setPageLoadError(error: unknown) {
  store.errorMessage = toStoreErrorMessage(
    error,
    CONFIG_STORE_MESSAGES.pageLoadFailed,
    CONFIG_STORE_MESSAGES.pageLoadForbidden
  )
}
const cloudReadonlyStatus = ref<CloudReadonlyStatus | null>(null)
const isLoadingCloudReadonlyStatus = ref(false)
const advancedConfigOpen = ref(false)

const activeRoutingModel = computed(
  () => store.routingModels.find((item) => item.isActive) ?? null
)
const modelOptions = computed(() =>
  store.languageModels
    .filter((model) => model.isEnabled)
    .map((model) => ({ label: `${model.name} / ${model.provider}`, value: model.id }))
)
const routingCandidateOptions = computed(() =>
  store.languageModels
    .filter((model) => model.isEnabled && model.usages.includes('Routing'))
    .map((model) => ({ label: `${model.name} / ${model.provider}`, value: model.id }))
)
const fixedSlots = computed(() =>
  slotDefinitions.map((definition) => {
    const template = findTemplate(definition)
    const model = findSlotModel(definition, template)
    return {
      ...definition,
      template,
      model,
      ready: Boolean(template && model && model.isEnabled && model.hasApiKey)
    }
  })
)
const primaryModel = computed(() => fixedSlots.value.find((slot) => slot.model)?.model ?? null)
const allSlotsReady = computed(() => fixedSlots.value.every((slot) => slot.ready))
const isTestingAnySlotModel = computed(() =>
  fixedSlots.value.some((slot) => isTestingModel(slot.model?.id))
)
const slotsUseSingleModel = computed(() => {
  const slots = fixedSlots.value
  if (slots.some((slot) => !slot.model)) {
    return false
  }

  const firstModel = slots[0]?.model
  if (!firstModel) {
    return false
  }

  return slots.every((slot) =>
    slot.model?.id === firstModel.id &&
    slot.model?.provider === firstModel.provider
  )
})
const sortedSkills = computed(() =>
  [...skillDefinitions.value].sort((a, b) =>
    Number(b.isBuiltIn) - Number(a.isBuiltIn) ||
    a.displayName.localeCompare(b.displayName, 'zh-CN'))
)

const usageChat = computed({
  get: () => store.currentLanguageModel.usages.includes('Chat'),
  set: (value: boolean) => toggleUsage('Chat', value)
})
const usageRouting = computed({
  get: () => store.currentLanguageModel.usages.includes('Routing'),
  set: (value: boolean) => toggleUsage('Routing', value)
})
const usagePlanner = computed({
  get: () => store.currentLanguageModel.usages.includes('Planner'),
  set: (value: boolean) => toggleUsage('Planner', value)
})

function findTemplate(slot: AgentSlotDefinition) {
  const code = slot.templateCode.toLowerCase()
  const exactCodeMatch = store.conversationTemplates.find(
    (template) => template.code?.toLowerCase() === code,
  )
  if (exactCodeMatch) return exactCodeMatch

  return (
    store.conversationTemplates.find((template) => template.scope === slot.templateScope) ?? null
  )
}

function findSlotModel(slot: AgentSlotDefinition, template?: ConversationTemplateSummary | null) {
  if (slot.key === 'intent' && activeRoutingModel.value) {
    return store.languageModels.find((model) => model.id === activeRoutingModel.value?.modelId) ?? null
  }

  if (template?.modelId) {
    const templateModel = store.languageModels.find((model) => model.id === template.modelId)
    if (templateModel) return templateModel
  }

  return store.languageModels.find((model) => model.isEnabled && model.usages.includes(slot.modelUsage)) ?? null
}

function modelLabel(model?: LanguageModelSummary | null) {
  return model ? `${model.name} / ${model.provider}` : '未配置'
}

function protocolLabel(protocolType?: string | null) {
  return protocolType ? protocolLabels[protocolType] ?? protocolType : '-'
}

function connectivityLabel(status?: string | null) {
  return status ? connectivityLabels[status] ?? status : '未测试'
}

function connectivityTone(status?: string | null) {
  if (status === 'Succeeded') return 'success'
  if (status === 'Failed') return 'danger'
  return 'neutral'
}

function cloudReadonlyStatusLabel(status?: string | null) {
  const labels: Record<string, string> = {
    Disabled: '未接入',
    Simulation: '模拟数据',
    RealReady: '正式只读可用',
    RealMissingBaseUrl: '缺少地址',
    RealMissingToken: '缺少凭据',
    RealNotAllowed: '未放行'
  }
  return status ? labels[status] ?? status : '未加载'
}

function cloudReadonlyStatusTone(status?: string | null) {
  if (status === 'RealReady') return 'success'
  if (status === 'Simulation') return 'warning'
  if (status === 'Disabled') return 'neutral'
  return 'danger'
}

function yesNo(value?: boolean | null) {
  return value ? '是' : '否'
}

function temperatureLabel(value?: number | null) {
  if (typeof value !== 'number') return '-'
  const formattedValue = Number(value.toFixed(2)).toString()
  if (value <= 0.2) return `稳定 (${formattedValue})`
  if (value <= 0.7) return `均衡 (${formattedValue})`
  return `创造 (${formattedValue})`
}

function mappedListText(values: string[], labels: Record<string, string>, empty = '未限制') {
  return values.length ? values.map((value) => labels[value] ?? value).join(' / ') : empty
}

function approvalPolicyLabel(value?: string | null) {
  if (!value) return '-'
  return approvalPolicyLabels[value] ?? value
}

function skillTone(skill: SkillDefinition) {
  if (!skill.isEnabled) return 'neutral'
  if (skill.skillCode === 'cloud_readonly') return 'warning'
  if (skill.riskLevel === 'Low') return 'success'
  return 'teal'
}

function skillDisplayDescription(skill: SkillDefinition) {
  return getSkillDisplayDescription(skill.skillCode)
}

function toolDisplayName(toolCode: string) {
  return (toolSummaries.value ?? []).find((tool) => tool.toolCode === toolCode)?.displayName ?? toolCode
}

async function refreshSkillDefinitions() {
  isLoadingSkills.value = true
  try {
    skillDefinitions.value = await configService.getSkills()
  } catch (error) {
    console.error('Failed to load skill definitions for config view.', error)
    skillDefinitions.value = []
    setPageLoadError(error)
    throw error
  } finally {
    isLoadingSkills.value = false
  }
}

async function refreshToolCatalog() {
  isLoadingToolCatalog.value = true
  try {
    const catalog = await configService.getToolCatalog()
    toolSummaries.value = Array.isArray(catalog.tools) ? catalog.tools : []
    toolCatalogLoaded.value = true
  } catch (error) {
    console.error('Failed to load tool catalog for config view.', error)
    toolSummaries.value = []
    setPageLoadError(error)
    throw error
  } finally {
    isLoadingToolCatalog.value = false
  }
}

async function ensureToolCatalogLoaded() {
  if (toolCatalogLoaded.value || isLoadingToolCatalog.value) {
    return
  }

  try {
    await refreshToolCatalog()
  } catch (error) {
    console.error('Tool catalog load failure already reported for config view.', error)
    // refreshToolCatalog already records the user-facing error.
  }
}

async function refreshCloudReadonlyStatus() {
  isLoadingCloudReadonlyStatus.value = true
  try {
    cloudReadonlyStatus.value = await configService.getCloudReadonlyStatus()
  } catch (error) {
    console.error('Failed to load Cloud readonly status for config view.', error)
    cloudReadonlyStatus.value = null
    setPageLoadError(error)
    throw error
  } finally {
    isLoadingCloudReadonlyStatus.value = false
  }
}

async function refreshAllAgentSettings() {
  store.errorMessage = ''
  const refreshers = [
    store.refreshAgentSlots(),
    refreshSkillDefinitions(),
    refreshCloudReadonlyStatus()
  ]

  if (advancedConfigOpen.value) {
    refreshers.push(refreshToolCatalog())
  }

  try {
    await Promise.all(refreshers)
  } catch (error) {
    console.error('Failed to refresh AI agent settings.', error)
    if (!store.errorMessage) {
      setPageLoadError(error)
    }
  }
}

function promptLength(template?: ConversationTemplateSummary | null) {
  return template?.systemPrompt?.trim().length ?? 0
}

function promptSummary(template?: ConversationTemplateSummary | null) {
  return template ? `${promptLength(template)} 字 · 已写入数据库` : ''
}

function templateScopeLabel(scope?: string | null) {
  return scope ? templateScopeLabels[scope] ?? scope : '-'
}

function setTestingModel(id: string, value: boolean) {
  const next = new Set(testingModelIds.value)
  if (value) next.add(id)
  else next.delete(id)
  testingModelIds.value = next
}

function isTestingModel(id?: string | null) {
  return Boolean(id && testingModelIds.value.has(id))
}

function toggleUsage(usage: LanguageModelUsage, enabled: boolean) {
  const next = new Set(store.currentLanguageModel.usages)
  if (enabled) next.add(usage)
  else next.delete(usage)
  store.currentLanguageModel.usages = [...next]
}

function onProtocolChange(protocolType: string) {
  if (protocolType !== 'AnthropicMessages') return
  store.currentLanguageModel.baseUrl = 'https://api.anthropic.com'
  if (!store.currentLanguageModel.provider || ['OpenAI', 'DeepSeek', '通义千问', '豆包'].includes(store.currentLanguageModel.provider)) {
    store.currentLanguageModel.provider = 'Anthropic'
  }
}

function showTestResult(result: LanguageModelTestResult) {
  if (result.success) {
    showAiToast('success', `配置完成，耗时 ${result.elapsedMilliseconds}ms`)
    return
  }
  showAiToast('error', result.error || result.message || '连接测试失败')
}

function buildCurrentLanguageModelTestPayload() {
  const form = store.currentLanguageModel
  return {
    id: form.id,
    provider: form.provider.trim(),
    protocolType: form.protocolType.trim(),
    name: form.name.trim(),
    baseUrl: form.baseUrl.trim(),
    apiKey: form.apiKeyAction === 'replace' ? form.apiKey.trim() : '',
    clearApiKey: form.apiKeyAction === 'clear',
    maxTokens: form.contextWindowTokens,
    contextWindowTokens: form.contextWindowTokens,
    maxOutputTokens: form.maxOutputTokens,
    usages: form.usages.length > 0 ? form.usages : (['Chat'] as LanguageModelUsage[]),
    temperature: form.temperature,
    persistResult: false
  }
}

async function testCurrentLanguageModel() {
  drawerTesting.value = true
  try {
    const result = await configService.testLanguageModel(buildCurrentLanguageModelTestPayload())
    showTestResult(result)
  } finally {
    drawerTesting.value = false
  }
}

async function testSlotModel(model?: LanguageModelSummary | null) {
  if (!model) return
  setTestingModel(model.id, true)
  try {
    const result = await configService.testLanguageModel({ id: model.id, persistResult: true })
    await store.refreshLanguageModels()
    showTestResult(result)
  } finally {
    setTestingModel(model.id, false)
  }
}

async function testAllSlotModels() {
  const slots = fixedSlots.value
  const modelIds = new Set(slots.map((slot) => slot.model?.id).filter(Boolean) as string[])
  modelIds.forEach((id) => setTestingModel(id, true))

  const failures: string[] = []
  try {
    for (const slot of slots) {
      if (!slot.model) {
        failures.push(`${slot.title}未配置模型`)
        continue
      }

      const result = await configService.testLanguageModel({ id: slot.model.id, persistResult: true })
      if (!result.success) {
        failures.push(`${slot.title}: ${result.error || result.message || '连接测试失败'}`)
      }
    }

    await store.refreshLanguageModels()
    if (failures.length) {
      showAiToast('error', failures.join('；'))
      return
    }

    showAiToast('success', '三个阶段连接测试均通过')
  } finally {
    modelIds.forEach((id) => setTestingModel(id, false))
  }
}

function openPrimaryModelDialog() {
  const defaultSlot = slotDefinitions.find((slot) => slot.key === 'executor') ?? slotDefinitions[0]
  if (!defaultSlot) return

  if (slotsUseSingleModel.value && primaryModel.value) {
    openModelDialog(defaultSlot, primaryModel.value)
    return
  }

  if (!primaryModel.value) {
    openModelDialog(defaultSlot)
    return
  }

  advancedConfigOpen.value = true
}

function onAdvancedConfigToggle(event: Event) {
  advancedConfigOpen.value = (event.currentTarget as HTMLDetailsElement).open
}

watch(advancedConfigOpen, (isOpen) => {
  if (isOpen) {
    void ensureToolCatalogLoaded()
  }
})

function openModelDialog(slot: AgentSlotDefinition, model?: LanguageModelSummary | null) {
  if (model) {
    void store.openEditLanguageModelDialog(model.id)
    return
  }

  store.openCreateLanguageModelDialog()
  store.currentLanguageModel.name = slot.defaultModelName
  store.currentLanguageModel.usages = [slot.modelUsage]
}

function openRoutingDialog(model?: LanguageModelSummary | null) {
  if (activeRoutingModel.value) {
    void store.openEditRoutingModelDialog(activeRoutingModel.value.id)
    return
  }

  store.openCreateRoutingModelDialog()
  store.currentRoutingModel.name = 'Intent Routing Agent'
  store.currentRoutingModel.modelId = model?.id || routingCandidateOptions.value[0]?.value?.toString() || ''
  store.currentRoutingModel.isActive = true
}

async function openTemplateDialog(slot: AgentSlotDefinition, template?: ConversationTemplateSummary | null, model?: LanguageModelSummary | null) {
  if (template) {
    void store.openEditConversationTemplateDialog(template.id)
    return
  }

  const modelId = model?.id || modelOptions.value[0]?.value?.toString()
  if (!modelId) {
    showAiToast('error', '请先配置一个可用模型，再播种内置提示词')
    return
  }

  await configService.resetBuiltInConversationTemplates(modelId)
  await store.refreshConversationTemplates()
  showAiToast('success', '内置提示词已从后端种子写入')

  const seededTemplate = findTemplate(slot)
  if (seededTemplate) {
    void store.openEditConversationTemplateDialog(seededTemplate.id)
  }
}

onMounted(() => {
  void refreshAllAgentSettings()
})
</script>

<template>
  <AppShell>
    <AiDataPage
      eyebrow="AI 设置"
      title="AI 配置"
      description="先维护主模型连接和 Cloud 只读状态；三阶段模型、提示词和 Skill 边界放在高级设置。"
    >
      <template #actions>
        <AiButton :disabled="store.isLoading || isLoadingSkills || isLoadingCloudReadonlyStatus" @click="refreshAllAgentSettings()">
          <RefreshCw class="h-4 w-4" :class="{ 'animate-spin': store.isLoading || isLoadingCloudReadonlyStatus }" />
          刷新
        </AiButton>
      </template>

      <div v-if="store.errorMessage" class="error-banner">{{ store.errorMessage }}</div>

      <div class="primary-config-grid">
        <AiCard class="primary-model-card" tone="teal" data-testid="primary-model-config-card">
          <header class="slot-head">
            <div class="slot-title">
              <Bot class="h-5 w-5" />
              <div>
                <h2>模型连接</h2>
                <p>普通运维只需要确认模型可用；内部三阶段配置默认收进高级设置。</p>
              </div>
            </div>
            <AiTag :tone="allSlotsReady ? 'success' : 'warning'">
              {{ allSlotsReady ? '可用' : '需完善' }}
            </AiTag>
          </header>

          <div v-if="slotsUseSingleModel && primaryModel" class="model-grid">
            <div>
              <span>服务商 / 模型</span>
              <strong>{{ modelLabel(primaryModel) }}</strong>
            </div>
            <div>
              <span>协议</span>
              <strong>{{ protocolLabel(primaryModel.protocolType) }}</strong>
            </div>
            <div>
              <span>上下文容量</span>
              <strong>{{ primaryModel.contextWindowTokens ?? '-' }}</strong>
            </div>
            <div>
              <span>回答长度</span>
              <strong>{{ primaryModel.maxOutputTokens ?? '-' }}</strong>
            </div>
            <div>
              <span>回答稳定性 / 创造性</span>
              <strong>{{ temperatureLabel(primaryModel.temperature) }}</strong>
            </div>
            <div>
              <span>连通性</span>
              <AiTag :tone="connectivityTone(primaryModel.connectivityStatus)">
                {{ connectivityLabel(primaryModel.connectivityStatus) }}
              </AiTag>
            </div>
          </div>
          <div v-else class="prompt-missing">
            高级配置：各阶段使用不同模型或尚未完全配置。展开高级设置查看意图识别、计划生成和最终执行的独立槽位。
          </div>

          <div class="action-row">
            <AiButton size="sm" @click="openPrimaryModelDialog()">
              {{ slotsUseSingleModel && primaryModel ? '编辑模型' : '查看高级配置' }}
            </AiButton>
            <AiButton size="sm" variant="lime" :disabled="isTestingAnySlotModel" @click="testAllSlotModels()">
              {{ isTestingAnySlotModel ? '测试中' : '测试连接' }}
            </AiButton>
            <AiButton size="sm" @click="advancedConfigOpen = true">
              高级设置
            </AiButton>
          </div>
        </AiCard>

        <AiCard class="cloud-status-card" tone="blue" data-testid="cloud-readonly-status-card">
          <header class="slot-head">
            <div class="slot-title">
              <Network class="h-5 w-5" />
              <div>
                <h2>Cloud 只读数据</h2>
                <p>可读取和分析已授权数据，禁止新增、修改、删除、审批或触发业务流程。</p>
              </div>
            </div>
            <AiTag :tone="cloudReadonlyStatusTone(cloudReadonlyStatus?.status)">
              {{ cloudReadonlyStatusLabel(cloudReadonlyStatus?.status) }}
            </AiTag>
          </header>

          <p class="cloud-status-message">
            {{ cloudReadonlyStatus?.message || '正在读取 Cloud 只读配置状态。' }}
          </p>

          <div class="model-grid">
            <div>
              <span>模式</span>
              <strong>{{ cloudReadonlyStatus?.mode || '-' }}</strong>
            </div>
            <div>
              <span>状态</span>
              <strong>{{ cloudReadonlyStatusLabel(cloudReadonlyStatus?.status) }}</strong>
            </div>
            <div>
              <span>BaseUrl 已配置</span>
              <strong>{{ yesNo(cloudReadonlyStatus?.baseUrlConfigured) }}</strong>
            </div>
            <div>
              <span>凭据已配置</span>
              <strong>{{ yesNo(cloudReadonlyStatus?.tokenConfigured) }}</strong>
            </div>
            <div>
              <span>正式只读放行</span>
              <strong>{{ yesNo(cloudReadonlyStatus?.productionReadAllowed) }}</strong>
            </div>
          </div>

          <div class="action-row">
            <AiButton size="sm" :disabled="isLoadingCloudReadonlyStatus" @click="refreshCloudReadonlyStatus()">
              <RefreshCw class="h-4 w-4" :class="{ 'animate-spin': isLoadingCloudReadonlyStatus }" />
              刷新状态
            </AiButton>
          </div>
        </AiCard>
      </div>

      <details class="advanced-config-fold" :open="advancedConfigOpen" @toggle="onAdvancedConfigToggle">
        <summary>
          <span>高级设置</span>
          <small>三阶段模型、System Prompt、Skill 工具边界</small>
        </summary>
        <div class="advanced-config-body">
          <div v-if="!slotsUseSingleModel" class="advanced-config-note">
            当前三阶段模型不完全一致，系统会保留独立配置。只有三个阶段的 modelId 和 provider 全部一致时，主界面才合并为单模型卡片。
          </div>

      <div class="slot-grid">
        <AiCard
          v-for="slot in fixedSlots"
          :key="slot.key"
          class="slot-card"
          :tone="slot.tone"
          :data-testid="`agent-slot-${slot.key}`"
        >
          <header class="slot-head">
            <div class="slot-title">
              <component :is="slot.icon" class="h-5 w-5" />
              <div>
                <h2>{{ slot.title }}</h2>
                <p>{{ slot.subtitle }}</p>
              </div>
            </div>
            <AiTag :tone="slot.ready ? 'success' : 'warning'">
              {{ slot.ready ? '已配置' : '需完善' }}
            </AiTag>
          </header>

          <section class="slot-section">
            <div class="section-title">
              <strong>模型</strong>
              <AiTag :tone="slot.model?.hasApiKey ? 'success' : 'neutral'">
                {{ slot.model?.hasApiKey ? '密钥已配置' : '密钥未配置' }}
              </AiTag>
            </div>
            <div class="model-grid">
              <div>
                <span>服务商 / 模型</span>
                <strong>{{ modelLabel(slot.model) }}</strong>
              </div>
              <div>
                <span>协议</span>
                <strong>{{ protocolLabel(slot.model?.protocolType) }}</strong>
              </div>
              <div>
                <span>上下文</span>
                <strong>{{ slot.model?.contextWindowTokens ?? '-' }}</strong>
              </div>
              <div>
                <span>最大输出</span>
                <strong>{{ slot.model?.maxOutputTokens ?? '-' }}</strong>
              </div>
              <div>
                <span>温度</span>
                <strong>{{ temperatureLabel(slot.model?.temperature) }}</strong>
              </div>
              <div>
                <span>连通性</span>
                <AiTag :tone="connectivityTone(slot.model?.connectivityStatus)">
                  {{ connectivityLabel(slot.model?.connectivityStatus) }}
                </AiTag>
              </div>
            </div>
            <div class="action-row">
              <AiButton size="sm" @click="openModelDialog(slot, slot.model)">
                {{ slot.model ? '编辑模型' : '新增模型' }}
              </AiButton>
              <AiButton size="sm" variant="lime" :disabled="!slot.model || isTestingModel(slot.model?.id)" @click="testSlotModel(slot.model)">
                {{ isTestingModel(slot.model?.id) ? '测试中' : '测试连接' }}
              </AiButton>
              <AiButton v-if="slot.key === 'intent'" size="sm" @click="openRoutingDialog(slot.model)">
                意图路由
              </AiButton>
            </div>
          </section>

          <section class="slot-section">
            <div class="section-title">
              <strong>系统提示词</strong>
              <AiTag :tone="slot.template?.isEnabled ? 'success' : 'warning'">
                {{ slot.template?.isEnabled ? '已配置' : '未配置' }}
              </AiTag>
            </div>
            <div v-if="slot.template" class="prompt-summary">
              <span>{{ promptSummary(slot.template) }}</span>
              <strong>{{ slot.template.isEnabled ? '启用中' : '已停用' }}</strong>
            </div>
            <details v-if="slot.template" class="slot-technical-fold">
              <summary>配置详情</summary>
              <div class="technical-grid">
                <div>
                  <span>模板名称</span>
                  <strong>{{ slot.template.name }}</strong>
                </div>
                <div>
                  <span>模板编码</span>
                  <strong>{{ slot.template.code || '-' }}</strong>
                </div>
                <div>
                  <span>作用域</span>
                  <strong>{{ templateScopeLabel(slot.template.scope) }}</strong>
                </div>
                <div>
                  <span>说明</span>
                  <strong>{{ slot.template.description || '-' }}</strong>
                </div>
              </div>
            </details>
            <details v-if="slot.template" class="prompt-preview-fold">
              <summary>预览提示词</summary>
              <pre class="prompt-preview">{{ slot.template.systemPrompt }}</pre>
            </details>
            <div v-else class="prompt-missing">
              内置提示词尚未播种。点击下方按钮从后端内置模板写入数据库。
            </div>
            <div class="action-row">
              <AiButton size="sm" @click="openTemplateDialog(slot, slot.template, slot.model)">
                {{ slot.template ? '编辑提示词' : '播种内置提示词' }}
              </AiButton>
            </div>
          </section>
        </AiCard>
      </div>

      <section class="skill-section" data-testid="skill-definition-section">
        <header class="skill-section-head">
          <div class="slot-title">
            <Sparkles class="h-5 w-5" />
            <div>
              <h2>Skill 能力</h2>
              <p>意图识别会自动选择 Skill，Skill 再收窄本次可用的数据源、知识库、工具和输出类型。</p>
            </div>
          </div>
          <AiButton size="sm" :disabled="isLoadingSkills" @click="refreshSkillDefinitions()">
            <RefreshCw class="h-4 w-4" :class="{ 'animate-spin': isLoadingSkills }" />
            刷新
          </AiButton>
        </header>

        <div class="skill-grid">
          <AiCard
            v-for="skill in sortedSkills"
            :key="skill.id"
            class="skill-card"
            :data-testid="`skill-card-${skill.skillCode}`"
          >
            <header class="skill-card-head">
              <div>
                <h3>{{ skill.displayName }}</h3>
                <code>{{ skill.skillCode }}</code>
              </div>
              <AiTag :tone="skillTone(skill)">
                {{ skill.isEnabled ? skill.riskLevel : '停用' }}
              </AiTag>
            </header>
            <p>{{ skillDisplayDescription(skill) }}</p>
            <div class="skill-meta-grid">
              <div>
                <span>数据源</span>
                <strong>{{ mappedListText(skill.allowedDataSourceModes, dataSourceModeLabels) }}</strong>
              </div>
              <div>
                <span>知识库</span>
                <strong>{{ mappedListText(skill.allowedKnowledgeScopes, knowledgeScopeLabels) }}</strong>
              </div>
              <div>
                <span>审批</span>
                <strong>{{ approvalPolicyLabel(skill.approvalPolicy) }}</strong>
              </div>
              <div>
                <span>输出</span>
                <strong>{{ mappedListText(skill.outputComponentTypes, outputComponentLabels, '文本') }}</strong>
              </div>
            </div>
            <details class="skill-tool-fold">
              <summary>允许工具 · {{ skill.allowedToolCodes.length }}</summary>
              <div class="tool-chip-list">
                <span
                  v-for="toolCode in skill.allowedToolCodes"
                  :key="`${skill.skillCode}:${toolCode}`"
                  class="tool-chip"
                >
                  <strong>{{ toolDisplayName(toolCode) }}</strong>
                  <code>{{ toolCode }}</code>
                </span>
              </div>
            </details>
          </AiCard>
        </div>
      </section>
        </div>
      </details>
    </AiDataPage>

    <AiDrawer v-model="store.dialogStates.languageModel" title="模型槽位" width="620px">
      <div class="ai-form">
        <label><span>名称</span><AiInput v-model="store.currentLanguageModel.name" /></label>
        <label><span>服务商</span><AiInput v-model="store.currentLanguageModel.provider" /></label>
        <label>
          <span>协议类型</span>
          <AiSelect
            v-model="store.currentLanguageModel.protocolType"
            :options="protocolOptions"
            @update:model-value="(value) => onProtocolChange(String(value ?? ''))"
          />
        </label>
        <label><span>接口地址</span><AiInput v-model="store.currentLanguageModel.baseUrl" /></label>
        <label>
          <span>密钥</span>
          <AiInput
            v-model="store.currentLanguageModel.apiKey"
            type="password"
            autocomplete="new-password"
            @update:model-value="store.currentLanguageModel.apiKeyAction = 'replace'"
          />
        </label>
        <AiCheckbox
          v-if="store.currentLanguageModel.hasApiKey"
          v-model="store.currentLanguageModel.clearApiKey"
          @update:model-value="store.currentLanguageModel.apiKeyAction = store.currentLanguageModel.clearApiKey ? 'clear' : 'keep'"
        >
          清除已有密钥
        </AiCheckbox>
        <div>
          <span class="field-label">用途</span>
          <div class="inline-options">
            <AiCheckbox v-model="usageRouting">{{ usageLabels.Routing }}</AiCheckbox>
            <AiCheckbox v-model="usagePlanner">{{ usageLabels.Planner }}</AiCheckbox>
            <AiCheckbox v-model="usageChat">{{ usageLabels.Chat }}</AiCheckbox>
          </div>
        </div>
        <div class="form-row"><span>启用</span><AiSwitch v-model="store.currentLanguageModel.isEnabled" /></div>
        <label><span>上下文窗口</span><AiNumberInput v-model="store.currentLanguageModel.contextWindowTokens" :min="1024" :step="1024" /></label>
        <label><span>最大输出</span><AiNumberInput v-model="store.currentLanguageModel.maxOutputTokens" :min="256" :step="256" /></label>
        <label><span>温度</span><AiNumberInput v-model="store.currentLanguageModel.temperature" :min="0" :max="2" :step="0.1" /></label>
        <div v-if="store.actionErrors.languageModel" class="drawer-error">{{ store.actionErrors.languageModel }}</div>
        <footer>
          <AiButton @click="store.closeLanguageModelDialog()">取消</AiButton>
          <AiButton :disabled="drawerTesting" @click="testCurrentLanguageModel()">
            {{ drawerTesting ? '测试中' : '测试连接' }}
          </AiButton>
          <AiButton variant="primary" :disabled="store.submittingStates.languageModel" @click="store.saveLanguageModel()">
            {{ store.submittingStates.languageModel ? '保存中' : '保存' }}
          </AiButton>
        </footer>
      </div>
    </AiDrawer>

    <AiDrawer v-model="store.dialogStates.routingModel" title="意图路由模型" width="520px">
      <div class="ai-form">
        <label><span>名称</span><AiInput v-model="store.currentRoutingModel.name" /></label>
        <label>
          <span>语言模型</span>
          <AiSelect v-model="store.currentRoutingModel.modelId" :options="routingCandidateOptions" placeholder="选择意图识别模型" />
        </label>
        <div class="form-row"><span>激活</span><AiSwitch v-model="store.currentRoutingModel.isActive" /></div>
        <div v-if="store.actionErrors.routingModel" class="drawer-error">{{ store.actionErrors.routingModel }}</div>
        <footer>
          <AiButton @click="store.closeRoutingModelDialog()">取消</AiButton>
          <AiButton variant="primary" :disabled="store.submittingStates.routingModel" @click="store.saveRoutingModel()">
            {{ store.submittingStates.routingModel ? '保存中' : '保存' }}
          </AiButton>
        </footer>
      </div>
    </AiDrawer>

    <AiDrawer v-model="store.dialogStates.conversationTemplate" title="系统提示词" width="760px">
      <div class="ai-form">
        <label><span>名称</span><AiInput v-model="store.currentConversationTemplate.name" /></label>
        <label><span>说明</span><AiInput v-model="store.currentConversationTemplate.description" /></label>
        <label>
          <span>绑定模型</span>
          <AiSelect v-model="store.currentConversationTemplate.modelId" :options="modelOptions" placeholder="选择模型" />
        </label>
        <div class="form-grid">
          <label><span>最大输出</span><AiNumberInput v-model="store.currentConversationTemplate.maxTokens" :min="256" :step="256" /></label>
          <label><span>温度</span><AiNumberInput v-model="store.currentConversationTemplate.temperature" :min="0" :max="2" :step="0.1" /></label>
        </div>
        <label><span>系统提示词</span><AiTextarea v-model="store.currentConversationTemplate.systemPrompt" :rows="16" /></label>
        <div class="form-row"><span>启用</span><AiSwitch v-model="store.currentConversationTemplate.isEnabled" /></div>
        <div v-if="store.actionErrors.conversationTemplate" class="drawer-error">{{ store.actionErrors.conversationTemplate }}</div>
        <footer>
          <AiButton @click="store.closeConversationTemplateDialog()">取消</AiButton>
          <AiButton variant="primary" :disabled="store.submittingStates.conversationTemplate" @click="store.saveConversationTemplate()">
            {{ store.submittingStates.conversationTemplate ? '保存中' : '保存' }}
          </AiButton>
        </footer>
      </div>
    </AiDrawer>
  </AppShell>
</template>

<style scoped>
.primary-config-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.1fr) minmax(340px, 0.9fr);
  gap: 16px;
}

.primary-model-card,
.cloud-status-card {
  display: grid;
  align-content: start;
  gap: 16px;
}

.cloud-status-message {
  margin: 0;
  color: var(--ai-text-muted);
  font-size: 13px;
  font-weight: 800;
  line-height: 1.7;
}

.advanced-config-fold {
  display: grid;
  gap: 14px;
  margin-top: 18px;
  border: 1px solid rgba(148, 163, 184, 0.22);
  border-radius: 18px;
  background: rgba(255, 255, 255, 0.64);
  box-shadow: var(--ai-shadow-xs);
}

.advanced-config-fold > summary {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  min-height: 54px;
  cursor: pointer;
  padding: 0 16px;
  color: var(--ai-text);
  font-weight: 950;
  list-style: none;
}

.advanced-config-fold > summary::-webkit-details-marker {
  display: none;
}

.advanced-config-fold > summary small {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.advanced-config-body {
  display: grid;
  gap: 16px;
  padding: 0 14px 14px;
}

.advanced-config-note {
  border: 1px solid rgba(245, 158, 11, 0.28);
  border-radius: 14px;
  padding: 12px 14px;
  background: rgba(255, 251, 235, 0.84);
  color: #92400e;
  font-size: 13px;
  font-weight: 850;
  line-height: 1.6;
}

.slot-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 16px;
}

.skill-section {
  display: grid;
  gap: 14px;
  margin-top: 18px;
}

.skill-section-head,
.skill-card-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.skill-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

.skill-card {
  display: grid;
  gap: 12px;
}

.skill-card h3 {
  margin: 0;
  color: var(--ai-text);
  font-size: 17px;
  font-weight: 950;
}

.skill-card code {
  color: var(--ai-accent);
  font-size: 12px;
  font-weight: 850;
}

.skill-card p {
  margin: 0;
  color: var(--ai-text-muted);
  font-size: 13px;
  font-weight: 750;
  line-height: 1.6;
}

.skill-meta-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
}

.skill-meta-grid div {
  display: grid;
  gap: 5px;
  min-width: 0;
  border: 1px solid rgba(148, 163, 184, 0.24);
  border-radius: 12px;
  padding: 10px;
  background: rgba(255, 255, 255, 0.78);
}

.skill-meta-grid span {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.skill-meta-grid strong {
  min-width: 0;
  overflow: hidden;
  color: var(--ai-text);
  font-size: 13px;
  font-weight: 900;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.skill-tool-fold summary {
  display: inline-flex;
  min-height: 32px;
  cursor: pointer;
  align-items: center;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 900;
}

.tool-chip-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.tool-chip {
  display: inline-flex;
  max-width: 100%;
  min-height: 34px;
  align-items: center;
  gap: 6px;
  border: 1px solid rgba(63, 111, 115, 0.16);
  border-radius: 999px;
  padding: 5px 8px;
  background: rgba(248, 250, 252, 0.84);
}

.tool-chip strong {
  min-width: 0;
  overflow: hidden;
  color: var(--ai-text);
  font-size: 12px;
  font-weight: 900;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.tool-chip-list code {
  color: var(--ai-text-muted);
  font-size: 11px;
}

.slot-card {
  display: grid;
  gap: 16px;
}

.slot-head,
.section-title,
.action-row,
.form-row,
.inline-options {
  display: flex;
  align-items: center;
}

.slot-head,
.section-title,
.form-row {
  justify-content: space-between;
  gap: 12px;
}

.slot-title {
  display: flex;
  gap: 12px;
  min-width: 0;
}

.slot-title h2 {
  margin: 0;
  color: var(--ai-text);
  font-size: 20px;
  font-weight: 950;
}

.slot-title p,
.model-grid span,
.field-label {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.slot-title p {
  margin: 5px 0 0;
  line-height: 1.5;
}

.slot-section {
  display: grid;
  gap: 12px;
  border: 1px solid rgba(255, 255, 255, 0.58);
  border-radius: 16px;
  padding: 14px;
  background: rgba(255, 255, 255, 0.68);
}

.section-title strong {
  color: var(--ai-text);
  font-size: 15px;
  font-weight: 900;
}

.model-grid,
.form-grid,
.technical-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
}

.model-grid div,
.technical-grid div {
  display: grid;
  gap: 5px;
  min-width: 0;
  border: 1px solid rgba(148, 163, 184, 0.24);
  border-radius: 12px;
  padding: 10px;
  background: rgba(255, 255, 255, 0.78);
}

.model-grid strong,
.technical-grid strong {
  min-width: 0;
  overflow: hidden;
  color: var(--ai-text);
  font-size: 14px;
  font-weight: 900;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.action-row {
  gap: 8px;
  flex-wrap: wrap;
}

.prompt-summary {
  display: grid;
  gap: 4px;
  min-width: 0;
  border: 1px solid rgba(148, 163, 184, 0.22);
  border-radius: 12px;
  padding: 12px;
  background: rgba(248, 250, 252, 0.82);
}

.prompt-summary span,
.prompt-summary strong {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.prompt-summary span {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.prompt-summary strong {
  color: var(--ai-text);
  font-size: 13px;
  font-weight: 900;
}

.slot-technical-fold,
.prompt-preview-fold {
  min-width: 0;
}

.slot-technical-fold summary,
.prompt-preview-fold summary {
  display: inline-flex;
  min-height: 32px;
  cursor: pointer;
  align-items: center;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 900;
}

.prompt-preview {
  max-height: 220px;
  overflow: auto;
  margin: 0;
  border: 1px solid rgba(148, 163, 184, 0.28);
  border-radius: 12px;
  padding: 12px;
  background: rgba(15, 23, 42, 0.92);
  color: #e5eef6;
  font-size: 12px;
  line-height: 1.7;
  white-space: pre-wrap;
  word-break: break-word;
}

.technical-grid span {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.prompt-missing {
  border: 1px dashed rgba(148, 163, 184, 0.42);
  border-radius: 12px;
  padding: 12px;
  background: rgba(248, 250, 252, 0.86);
  color: var(--ai-text-muted);
  font-size: 13px;
  font-weight: 800;
  line-height: 1.6;
}

.error-banner,
.drawer-error {
  border: 1px solid #fecaca;
  border-radius: 14px;
  background: #fef2f2;
  padding: 12px 14px;
  color: #b42318;
  font-size: 13px;
  font-weight: 800;
}

.field-label {
  display: block;
  margin-bottom: 7px;
}

.inline-options {
  gap: 12px;
  flex-wrap: wrap;
}

.ai-form footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
}

@media (max-width: 1240px) {
  .primary-config-grid,
  .slot-grid,
  .skill-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 760px) {
  .model-grid,
  .form-grid,
  .technical-grid,
  .skill-meta-grid {
    grid-template-columns: 1fr;
  }

  .slot-head,
  .skill-section-head,
  .section-title,
  .advanced-config-fold > summary,
  .form-row {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
