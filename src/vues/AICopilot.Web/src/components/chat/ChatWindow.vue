<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  ArrowUp,
  Check,
  ChevronRight,
  Download,
  Eye,
  FileUp,
  FolderOpen,
  History,
  ListChecks,
  PanelLeftOpen,
  PanelRightOpen,
  Play,
  RefreshCw,
  Send,
  ShieldCheck,
  Sparkles,
  TriangleAlert,
  X
} from 'lucide-vue-next'
import AiTag from '@/components/ai/AiTag.vue'
import { useAgentWorkbench } from '@/composables/useAgentWorkbench'
import { useChatStore } from '@/stores/chatStore'
import { useUiLayoutStore, type AgentWorkbenchTab } from '@/stores/uiLayoutStore'
import MessageItem from './MessageItem.vue'
import SessionList from './SessionList.vue'

const store = useChatStore()
const uiLayoutStore = useUiLayoutStore()
const inputValue = ref('')
const agentGoal = ref('')
const cloudSandboxControlledGoal = ref('')
const cloudProductionControlledGoal = ref('')
const selectedTrialScenarioId = ref('')
const fileInput = ref<HTMLInputElement | null>(null)
const scrollContainer = ref<HTMLElement | null>(null)
const isMobile = ref(typeof window !== 'undefined' ? window.innerWidth < 1024 : false)
const sessionDrawerVisible = ref(false)

type AgentPlanPreview = {
  plannerMode?: string
  plannerFallbackReason?: string | null
  plannerToolCatalogVersion?: number
  plannerAvailableToolCount?: number
  toolCatalogVersion?: number
  visibleToolCount?: number
  toolRiskSummary?: Record<string, number>
  mockMcpOnly?: boolean
  toolApprovalCheckpoints?: string[]
  businessDomains?: string[]
  artifactTypes?: string[]
  forcedStepCodes?: string[]
  approvalCheckpoints?: string[]
  trialScenarioId?: string | null
  trialScenarioTitle?: string | null
  isSimulationTrial?: boolean
  isCloudSandboxControlledTrial?: boolean
  cloudSandboxGoalIntent?: {
    intentId?: string
    endpointCodes?: string[]
    maxRows?: number
    analysisType?: string
    requiresToolApproval?: boolean
    requiresFinalApproval?: boolean
    boundary?: string
    timeRange?: {
      from?: string | null
      to?: string | null
    }
  } | null
  isCloudProductionControlledPilotTrial?: boolean
  cloudProductionGoalIntent?: {
    intentId?: string
    endpointCodes?: string[]
    maxRows?: number
    analysisType?: string
    requiresToolApproval?: boolean
    requiresFinalApproval?: boolean
    timeRange?: {
      from?: string | null
      to?: string | null
    }
  } | null
  queryMode?: string | null
  dataSourceSummaries?: Array<{
    name?: string
    sourceMode?: string
    isSimulation?: boolean
    sourceLabel?: string
    businessDomain?: string | null
  }>
  plannerSafetySummary?: {
    planSource?: string
    plannerMode?: string
    plannerModelSummary?: string | null
    plannerToolCatalogVersion?: number
    availableToolCount?: number
    isSimulationOnly?: boolean
    requiresDataApproval?: boolean
    toolRiskSummary?: Record<string, number>
    mockMcpOnly?: boolean
  }
}

const {
  latestTask,
  taskHistory,
  taskSteps,
  taskArtifacts,
  draftArtifacts,
  finalArtifacts,
  currentArtifactPreview,
  pendingAgentApprovals,
  auditSummary,
  completedStepCount,
  blockedStep,
  workspaceFileCount,
  draftArtifactCount,
  finalArtifactCount,
  taskStatus,
  workspaceStatus,
  approvalGroups,
  artifactGroups,
  chartBars,
  onsiteStatus,
  agentStageCards,
  approvalCount,
  widgetCount,
  canCreatePlan,
  canRunTask,
  canContinueTask,
  canSubmitFinalReview,
  canFinalizeWorkspace,
  loginSource,
  cloudEmployeeNo
} = useAgentWorkbench()

const currentTitle = computed(() => store.currentSession?.title || '新会话')
const isInputDisabled = computed(() => store.isStreaming || store.isWaitingForApproval || !store.selectedModelId)
const trialScenarios = computed(() => store.agentTrialScenarios)
const selectedTrialScenario = computed(() =>
  trialScenarios.value.find((scenario) => scenario.id === selectedTrialScenarioId.value) ?? null
)
const latestPlan = computed<AgentPlanPreview | null>(() => parseAgentPlan(latestTask.value?.planJson))
const latestPlanMode = computed(() => latestPlan.value?.plannerMode || 'Pending')
const latestPlanDataSource = computed(() => latestPlan.value?.dataSourceSummaries?.[0] ?? null)
const latestPlanApprovalCount = computed(() => latestPlan.value?.approvalCheckpoints?.length ?? 0)
const latestPlanForcedSteps = computed(() => latestPlan.value?.forcedStepCodes ?? [])
const latestPlanToolCatalogVersion = computed(
  () => latestPlan.value?.toolCatalogVersion ?? latestPlan.value?.plannerToolCatalogVersion
)
const latestPlanVisibleToolCount = computed(
  () => latestPlan.value?.visibleToolCount ?? latestPlan.value?.plannerAvailableToolCount ?? 0
)
const latestPlanRiskSummary = computed(
  () => latestPlan.value?.toolRiskSummary ?? latestPlan.value?.plannerSafetySummary?.toolRiskSummary ?? {}
)
const latestPlanRiskLine = computed(() => {
  const entries = Object.entries(latestPlanRiskSummary.value)
  return entries.length ? entries.map(([risk, count]) => `${risk}:${count}`).join(' / ') : 'No risk summary'
})
const latestPlanMockMcpOnly = computed(
  () => latestPlan.value?.mockMcpOnly ?? latestPlan.value?.plannerSafetySummary?.mockMcpOnly ?? false
)
const latestPlanIsCloudSandbox = computed(
  () =>
    latestPlan.value?.queryMode === 'CloudReadonlySandbox' ||
    latestPlan.value?.isCloudSandboxControlledTrial === true ||
    latestPlan.value?.trialScenarioId?.startsWith('cloud-sandbox-')
)
const latestPlanIsCloudSandboxControlled = computed(
  () => latestPlan.value?.isCloudSandboxControlledTrial === true
)
const latestCloudSandboxIntent = computed(() => latestPlan.value?.cloudSandboxGoalIntent ?? null)
const trialCampaign = computed(() => store.currentTrialCampaign)
const trialSummary = computed(() => trialCampaign.value?.summary ?? null)
const trialReadiness = computed(() => store.currentPilotReadiness)
const trialEvidencePackage = computed(() => store.currentTrialEvidencePackage)
const cloudReadonlyPilotReadiness = computed(() => store.currentCloudReadonlyPilotReadiness)
const cloudReadonlyPilotConfigPackage = computed(() =>
  store.currentCloudReadonlyPilotConfigPackage ?? cloudReadonlyPilotReadiness.value?.configSummary ?? null
)
const pilotApprovalRehearsal = computed(() => store.currentPilotApprovalRehearsal)
const pilotContractRehearsal = computed(() => store.currentPilotContractRehearsal)
const cloudReadonlyProductionPilotStatus = computed(() => store.currentCloudReadonlyProductionPilotStatus)
const cloudReadonlyProductionPilotWindow = computed(() => store.currentCloudReadonlyProductionPilotWindow)
const cloudReadonlyProductionPilotRun = computed(() => store.currentCloudReadonlyProductionPilotRun)
const cloudReadonlyProductionControlledStatus = computed(() => store.currentCloudReadonlyProductionControlledStatus)
const cloudReadonlyProductionControlledPlan = computed(() => store.currentCloudReadonlyProductionControlledPlan)
const cloudReadonlyProductionControlledRun = computed(() => store.currentCloudReadonlyProductionControlledRun)
const cloudReadonlyProductionOperationsStatus = computed(() => store.currentCloudReadonlyProductionOperationsStatus)
const productionPilotRunLedger = computed(() => store.currentProductionPilotRunLedger)
const productionPilotGaReadiness = computed(() => store.currentProductionPilotGaReadiness)
const cloudProductionControlledIntent = computed(
  () =>
    cloudReadonlyProductionControlledPlan.value?.intent ??
    latestPlan.value?.cloudProductionGoalIntent ??
    null
)

function parseAgentPlan(planJson?: string | null): AgentPlanPreview | null {
  if (!planJson) return null
  try {
    const parsed = JSON.parse(planJson) as AgentPlanPreview
    return parsed && typeof parsed === 'object' ? parsed : null
  } catch {
    return null
  }
}

const suggestions = [
  '查看 DEV-001 最近 24 小时设备日志，并给出根因线索',
  '列出 LINE-A 当前设备状态，生成关键指标和记录摘要',
  '查询 DEV-001 配方版本历史，只做只读分析',
  '根据最近产能数据说明异常波动，不执行任何控制动作'
]

const agentTabs = computed<Array<{ value: AgentWorkbenchTab; label: string; count?: number }>>(() => [
  { value: 'plan', label: '计划', count: latestTask.value ? 1 : 0 },
  { value: 'steps', label: '步骤', count: taskSteps.value.length },
  { value: 'approvals', label: '审批', count: pendingAgentApprovals.value.length },
  { value: 'artifacts', label: '产物', count: taskArtifacts.value.length },
  { value: 'audit', label: '审计', count: auditSummary.value.length },
  { value: 'trial', label: '试用', count: trialSummary.value?.scenarioRunCount ?? store.trialCampaigns.length },
  { value: 'boundary', label: '边界' }
])

function statusTone(type?: string) {
  if (type === 'success') return 'success'
  if (type === 'warning') return 'warning'
  if (type === 'danger' || type === 'error') return 'danger'
  return 'neutral'
}

function setAgentTab(tab: AgentWorkbenchTab) {
  uiLayoutStore.setAgentWorkbenchTab(tab)
}

function handleModelChange(event: Event) {
  const value = (event.target as HTMLSelectElement).value
  store.setSelectedModel(value || null)
}

async function sendMessage() {
  const content = inputValue.value.trim()
  if (!content || isInputDisabled.value) return
  inputValue.value = ''
  await store.sendMessage(content)
}

function handleComposerKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault()
    void sendMessage()
  }
}

async function useSuggestion(text: string) {
  inputValue.value = text
  await sendMessage()
}

function openFilePicker() {
  fileInput.value?.click()
}

async function handleFileChange(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (!file) return
  await store.uploadSessionFile(file)
  target.value = ''
}

async function createAgentPlan() {
  const goal = agentGoal.value.trim() || inputValue.value.trim()
  if (!goal || !canCreatePlan.value) return
  agentGoal.value = goal
  await store.planAgentTask(goal)
}

function selectTrialScenario(scenarioId: string) {
  selectedTrialScenarioId.value = scenarioId
  const scenario = selectedTrialScenario.value
  if (scenario) {
    agentGoal.value = scenario.defaultPrompt
  }
}

async function createTrialScenarioPlan() {
  const scenario = selectedTrialScenario.value
  if (!scenario) return
  const task = await store.createAgentTaskFromTrialScenario(
    scenario,
    agentGoal.value.trim() || scenario.defaultPrompt
  )
  if (task) {
    uiLayoutStore.suggestAgentWorkbenchTab('approvals')
  }
}

async function createTrialCampaign() {
  await store.createTrialCampaign()
  uiLayoutStore.suggestAgentWorkbenchTab('trial')
}

async function attachLatestTaskToTrialCampaign() {
  await store.attachLatestTaskToTrialCampaign()
  uiLayoutStore.suggestAgentWorkbenchTab('trial')
}

async function runPilotReadinessEvaluation() {
  await store.runPilotReadinessEvaluation()
  uiLayoutStore.suggestAgentWorkbenchTab('trial')
}

async function generateTrialEvidencePackage() {
  await store.generateTrialEvidencePackage()
  uiLayoutStore.suggestAgentWorkbenchTab('trial')
}

async function createCloudReadonlyPilotConfigPackage() {
  await store.createCloudReadonlyPilotConfigPackage()
  uiLayoutStore.suggestAgentWorkbenchTab('trial')
}

async function runCloudReadonlyPilotGateEvaluation() {
  await store.runCloudReadonlyPilotGateEvaluation()
  uiLayoutStore.suggestAgentWorkbenchTab('trial')
}

async function runCloudReadonlyPilotApprovalRehearsal() {
  await store.runCloudReadonlyPilotApprovalRehearsal()
  uiLayoutStore.suggestAgentWorkbenchTab('trial')
}

async function runCloudReadonlyPilotContractRehearsal() {
  await store.runCloudReadonlyPilotContractRehearsal()
  uiLayoutStore.suggestAgentWorkbenchTab('trial')
}

async function createCloudReadonlyProductionPilotWindow() {
  await store.createCloudReadonlyProductionPilotWindow()
  uiLayoutStore.suggestAgentWorkbenchTab('trial')
}

async function approveCloudReadonlyProductionPilotWindow() {
  await store.approveCloudReadonlyProductionPilotWindow()
  uiLayoutStore.suggestAgentWorkbenchTab('trial')
}

async function runCloudReadonlyProductionPilotGate() {
  await store.runCloudReadonlyProductionPilotGate()
  uiLayoutStore.suggestAgentWorkbenchTab('trial')
}

async function runCloudReadonlyProductionPilotScenario() {
  await store.runCloudReadonlyProductionPilotScenario()
  uiLayoutStore.suggestAgentWorkbenchTab('trial')
}

async function createCloudReadonlyProductionControlledPlan() {
  const goal = cloudProductionControlledGoal.value.trim() || agentGoal.value.trim() || inputValue.value.trim()
  if (!goal || store.isAgentBusy) return
  cloudProductionControlledGoal.value = goal
  const result = await store.createCloudReadonlyProductionControlledPlan(goal)
  if (result) {
    agentGoal.value = goal
    uiLayoutStore.suggestAgentWorkbenchTab('approvals')
  }
}

async function runCloudReadonlyProductionControlledPilot() {
  await store.runCloudReadonlyProductionControlledPilot()
  uiLayoutStore.suggestAgentWorkbenchTab('trial')
}

async function activateProductionPilotEmergencyStop() {
  await store.activateProductionPilotEmergencyStop()
  uiLayoutStore.suggestAgentWorkbenchTab('trial')
}

async function clearProductionPilotEmergencyStop() {
  await store.clearProductionPilotEmergencyStop()
  uiLayoutStore.suggestAgentWorkbenchTab('trial')
}

async function createProductionPilotIncident() {
  await store.createProductionPilotIncident()
  uiLayoutStore.suggestAgentWorkbenchTab('trial')
}

async function runProductionPilotGaReadinessEvaluation() {
  await store.runProductionPilotGaReadinessEvaluation()
  uiLayoutStore.suggestAgentWorkbenchTab('trial')
}

async function createCloudSandboxControlledPlan() {
  const goal = cloudSandboxControlledGoal.value.trim() || agentGoal.value.trim() || inputValue.value.trim()
  if (!goal || store.isAgentBusy) return
  cloudSandboxControlledGoal.value = goal
  const result = await store.createCloudSandboxControlledPlan(goal)
  if (result) {
    agentGoal.value = goal
    uiLayoutStore.suggestAgentWorkbenchTab('approvals')
  }
}

async function runLatestTask() {
  if (!latestTask.value || !canRunTask.value) return
  await store.runAgentTask(latestTask.value.id)
}

async function submitFinalReview() {
  const code = store.currentWorkspace?.workspaceCode || latestTask.value?.workspaceCode
  if (!code || !canSubmitFinalReview.value) return
  await store.submitFinalReview(code)
}

async function finalizeCurrentWorkspace() {
  const code = store.currentWorkspace?.workspaceCode || latestTask.value?.workspaceCode
  if (!code || !canFinalizeWorkspace.value) return
  if (!window.confirm('确认后会把当前草稿产物写入 final/，作为正式输出。')) return
  await store.finalizeWorkspace(code)
}

async function continueLatestTask() {
  if (!latestTask.value || !canContinueTask.value) return
  await store.runAgentTask(latestTask.value.id)
}

async function refreshAuditSummary() {
  if (!latestTask.value) return
  await store.loadAgentAuditSummary(latestTask.value.id)
}

async function approveAgentApproval(approvalId: string) {
  const approval = pendingAgentApprovals.value.find((item) => item.id === approvalId)
  if (!approval) return
  const decided = await store.decideAgentApproval(approval, 'approve', 'Approved from agent workbench')
  if (decided && approval.type === 'Plan') {
    await store.runAgentTask(approval.taskId)
  }
}

async function rejectAgentApproval(approvalId: string) {
  const approval = pendingAgentApprovals.value.find((item) => item.id === approvalId)
  if (!approval) return
  const reason = window.prompt('请输入驳回原因', '需要修改计划或产物')
  if (reason === null) return
  await store.decideAgentApproval(approval, 'reject', reason)
}

async function downloadArtifact(artifactId: string) {
  const artifact = taskArtifacts.value.find((item) => item.id === artifactId)
  if (!artifact) return
  await store.downloadArtifact(artifact)
}

async function previewArtifact(artifactId: string) {
  await store.loadArtifactPreview(artifactId)
}

async function scrollToBottom() {
  await nextTick()
  if (scrollContainer.value) {
    scrollContainer.value.scrollTop = scrollContainer.value.scrollHeight
  }
}

function handleResize() {
  isMobile.value = window.innerWidth < 1024
  if (!isMobile.value) {
    sessionDrawerVisible.value = false
    uiLayoutStore.setAgentWorkbenchDrawerOpen(false)
  }
}

watch(
  [() => pendingAgentApprovals.value.length, () => taskSteps.value.map((step) => step.status).join(','), () => taskArtifacts.value.length],
  () => {
    if (pendingAgentApprovals.value.length > 0) {
      uiLayoutStore.suggestAgentWorkbenchTab('approvals')
      return
    }

    if (taskSteps.value.some((step) => step.status === 'Running' || step.status === 'WaitingApproval')) {
      uiLayoutStore.suggestAgentWorkbenchTab('steps')
      return
    }

    if (taskArtifacts.value.length > 0) {
      uiLayoutStore.suggestAgentWorkbenchTab('artifacts')
    }
  },
  { immediate: true }
)

watch(() => store.currentMessages, () => void scrollToBottom(), { deep: true })
watch(() => store.currentSessionId, () => {
  sessionDrawerVisible.value = false
  uiLayoutStore.setAgentWorkbenchDrawerOpen(false)
  uiLayoutStore.unpinAgentWorkbenchTab()
  void scrollToBottom()
})

onMounted(() => window.addEventListener('resize', handleResize))
onBeforeUnmount(() => window.removeEventListener('resize', handleResize))
</script>

<template>
  <div class="command-workbench">
    <aside v-if="!isMobile" class="session-task-rail" :class="{ collapsed: uiLayoutStore.isSessionRailCollapsed }">
      <div class="rail-head">
        <div>
          <span>SESSION RAIL</span>
          <strong>会话与任务</strong>
        </div>
        <button type="button" aria-label="折叠会话栏" @click="uiLayoutStore.toggleSessionRail()">
          <PanelLeftOpen :size="18" />
        </button>
      </div>
      <SessionList class="sessions" />
      <section class="task-history">
        <header>
          <History :size="18" />
          <div>
            <strong>任务历史</strong>
            <span>{{ taskHistory.length }} 个 Agent 任务</span>
          </div>
        </header>
        <div class="task-history-list">
          <button
            v-for="task in taskHistory"
            :key="task.id"
            class="task-history-item"
            :class="{ active: latestTask?.id === task.id }"
            type="button"
          >
            <strong>{{ task.title }}</strong>
            <span>{{ task.status }} · {{ task.workspaceCode || '未创建工作区' }}</span>
          </button>
          <div v-if="taskHistory.length === 0" class="rail-empty">
            当前会话还没有 Agent 任务
          </div>
        </div>
      </section>
    </aside>

    <section class="ai-canvas">
      <header class="canvas-header">
        <div class="title-zone">
          <button v-if="isMobile" class="icon-button" type="button" aria-label="打开会话" @click="sessionDrawerVisible = true">
            <PanelLeftOpen :size="20" />
          </button>
          <div>
            <p class="canvas-kicker">AI CANVAS</p>
            <h1>{{ currentTitle }}</h1>
          </div>
        </div>
        <div class="canvas-toolbar">
          <AiTag :tone="store.isStreaming ? 'warning' : 'success'">
            {{ store.isStreaming ? '生成中' : '就绪' }}
          </AiTag>
          <select
            class="model-selector"
            :value="store.selectedModelId ?? ''"
            :disabled="store.isStreaming"
            aria-label="选择模型"
            @change="handleModelChange"
          >
            <option value="">选择模型</option>
            <option v-for="model in store.chatModels" :key="model.id" :value="model.id">
              {{ model.provider }} / {{ model.name }}
            </option>
          </select>
          <button
            class="soft-action"
            type="button"
            :disabled="!store.currentSessionId"
            @click="store.currentSessionId && store.selectSession(store.currentSessionId, true)"
          >
            <RefreshCw :size="17" />
            刷新
          </button>
          <button
            v-if="isMobile"
            class="soft-action"
            type="button"
            @click="uiLayoutStore.setAgentWorkbenchDrawerOpen(true)"
          >
            <PanelRightOpen :size="17" />
            Agent
          </button>
        </div>
      </header>

      <div class="canvas-status-strip">
        <div v-for="item in agentStageCards" :key="item.label" class="status-tile">
          <span>{{ item.label }}</span>
          <AiTag :tone="statusTone(item.type)">{{ item.value }}</AiTag>
        </div>
        <div class="status-tile">
          <span>草稿 / 正式</span>
          <strong class="ai-number">{{ draftArtifactCount }}/{{ finalArtifactCount }}</strong>
        </div>
      </div>

      <div ref="scrollContainer" class="message-viewport">
        <div v-if="store.errorMessage" class="canvas-error" role="alert">
          <TriangleAlert :size="18" />
          {{ store.errorMessage }}
        </div>

        <div v-if="store.isLoadingHistory" class="loading-lines">
          <i />
          <i />
          <i />
          <i />
        </div>

        <section v-if="store.currentMessages.length === 0 && !store.isLoadingHistory" class="empty-chat">
          <Sparkles :size="28" />
          <h2>开始一次只读分析</h2>
          <p>选择真实工作场景，或直接输入设备、日志、配方、产能、知识库问题。</p>
          <div class="suggestions">
            <button v-for="item in suggestions" :key="item" type="button" @click="useSuggestion(item)">
              {{ item }}
              <ChevronRight :size="17" />
            </button>
          </div>
        </section>

        <div class="message-list">
          <MessageItem v-for="message in store.currentMessages" :key="message.timestamp" :message="message" />
        </div>
      </div>

      <footer class="command-composer">
        <div class="composer-tools">
          <input ref="fileInput" class="hidden-file" type="file" @change="handleFileChange">
          <button class="tool-button" type="button" :disabled="store.isAgentBusy" @click="openFilePicker">
            <FileUp :size="17" />
            上传
          </button>
          <button class="tool-button" type="button" :disabled="!canCreatePlan || store.isAgentBusy" @click="createAgentPlan">
            <ListChecks :size="17" />
            计划
          </button>
          <span v-if="store.uploadedFiles.length" class="uploaded-hint">
            {{ store.uploadedFiles.length }} 个输入文件
          </span>
        </div>
        <div class="composer-input-row">
          <textarea
            v-model="inputValue"
            :disabled="isInputDisabled"
            :placeholder="!store.selectedModelId ? '没有可用对话模型' : store.isWaitingForApproval ? '请先处理待审批请求' : '输入问题，Enter 发送，Shift + Enter 换行'"
            rows="1"
            @keydown="handleComposerKeydown"
          />
          <button class="send-button" type="button" :disabled="isInputDisabled || !inputValue.trim()" aria-label="发送" @click="sendMessage">
            <Send :size="19" />
          </button>
        </div>
      </footer>
    </section>

    <aside class="agent-workbench-panel" :class="{ 'mobile-open': isMobile && uiLayoutStore.isAgentWorkbenchDrawerOpen }">
      <div class="agent-panel-head">
        <div>
          <span>AGENT WORKBENCH</span>
          <strong>任务控制台</strong>
        </div>
        <button
          v-if="isMobile"
          class="drawer-close"
          type="button"
          aria-label="关闭 Agent 工作台"
          @click="uiLayoutStore.setAgentWorkbenchDrawerOpen(false)"
        >
          <X :size="18" />
        </button>
        <AiTag v-if="!isMobile" :tone="latestPlanIsCloudSandbox ? 'warning' : 'success'">
          {{ latestPlanIsCloudSandbox ? 'Cloud Sandbox Trial' : 'SimulationBusiness' }}
        </AiTag>
        <AiTag v-else tone="dark">Cloud 只读</AiTag>
      </div>

      <div class="agent-tabs" role="tablist" aria-label="Agent 工作台">
        <button
          v-for="tab in agentTabs"
          :key="tab.value"
          type="button"
          :data-testid="`agent-tab-${tab.value}`"
          :class="{ active: uiLayoutStore.agentWorkbenchTab === tab.value }"
          @click="setAgentTab(tab.value)"
        >
          {{ tab.label }}
          <span v-if="tab.count !== undefined">{{ tab.count }}</span>
        </button>
      </div>

      <div class="agent-tab-content">
        <section v-if="uiLayoutStore.agentWorkbenchTab === 'plan'" class="tab-stack">
          <div class="trial-scenario-panel">
            <div class="section-title">
              <strong>试用模板</strong>
              <AiTag tone="success">SimulationBusiness</AiTag>
              <AiTag tone="warning">Cloud Sandbox Trial</AiTag>
            </div>
            <div v-if="trialScenarios.length" class="trial-scenario-list">
              <button
                v-for="scenario in trialScenarios"
                :key="scenario.id"
                type="button"
                class="trial-scenario-card"
                :class="{ active: selectedTrialScenarioId === scenario.id }"
                @click="selectTrialScenario(scenario.id)"
              >
                <span>{{ scenario.businessDomain }}</span>
                <strong>{{ scenario.title }}</strong>
                <small>{{ scenario.description }}</small>
                <AiTag :tone="scenario.isCloudSandboxTrial ? 'warning' : 'success'">
                  {{ scenario.sourceMode || (scenario.isCloudSandboxTrial ? 'CloudReadonlySandbox' : 'SimulationBusiness') }}
                </AiTag>
                <em>{{ scenario.defaultArtifactTypes.join(' / ') }}</em>
              </button>
            </div>
            <div v-else class="panel-empty">
              {{ store.isLoadingAgentTrialScenarios ? '正在加载试用模板' : '暂无可用 SimulationBusiness 试用模板' }}
            </div>
            <button
              class="wide-action"
              type="button"
              :disabled="!selectedTrialScenario || store.isAgentBusy"
              @click="createTrialScenarioPlan"
            >
              <ListChecks :size="17" />
              生成模板计划
            </button>
          </div>
          <div class="trial-scenario-panel controlled-sandbox-panel">
            <div class="section-title">
              <strong>Cloud Sandbox Controlled Trial</strong>
              <AiTag tone="warning">非生产</AiTag>
            </div>
            <textarea
              v-model="cloudSandboxControlledGoal"
              class="agent-goal compact"
              rows="3"
              placeholder="输入受控自由目标，例如：分析最近一周产能交付风险"
            />
            <div class="planner-chip-row">
              <span>devices</span>
              <span>capacity_summary</span>
              <span>device_logs</span>
              <span>pass_station_records</span>
            </div>
            <button
              class="wide-action"
              type="button"
              :disabled="store.isAgentBusy || !(cloudSandboxControlledGoal.trim() || agentGoal.trim() || inputValue.trim())"
              @click="createCloudSandboxControlledPlan"
            >
              <ShieldCheck :size="17" />
              生成受控目标计划
            </button>
          </div>
          <div class="metric-grid">
            <div>
              <span>步骤</span>
              <strong>{{ completedStepCount }}/{{ taskSteps.length }}</strong>
            </div>
            <div>
              <span>工作区</span>
              <strong>{{ workspaceFileCount }}</strong>
            </div>
            <div>
              <span>审批</span>
              <strong>{{ pendingAgentApprovals.length }}</strong>
            </div>
          </div>
          <div v-if="latestPlan" class="planner-preview">
            <div class="planner-preview-head">
              <div>
                <span>Planner</span>
                <strong>{{ latestPlanMode }}</strong>
              </div>
              <AiTag :tone="latestPlanMode === 'Dynamic' ? 'success' : latestPlanMode === 'StaticFallback' ? 'warning' : 'neutral'">
                {{ latestPlan?.plannerSafetySummary?.planSource || latestPlan?.trialScenarioTitle || 'FreeGoal' }}
              </AiTag>
            </div>
            <div class="planner-preview-grid">
              <div>
                <span>Tool Catalog</span>
                <strong>v{{ latestPlanToolCatalogVersion ?? '-' }} / {{ latestPlanVisibleToolCount }}</strong>
              </div>
              <div>
                <span>Data Source</span>
                <strong>{{ latestPlanIsCloudSandboxControlled ? 'CloudReadonlySandbox / ControlledGoal' : latestPlanIsCloudSandbox ? 'CloudReadonlySandbox' : latestPlanDataSource?.sourceMode || 'SimulationBusiness' }}</strong>
              </div>
              <div>
                <span>Approval Points</span>
                <strong>{{ latestPlanApprovalCount }}</strong>
              </div>
              <div>
                <span>Mock MCP</span>
                <strong>{{ latestPlanMockMcpOnly ? 'Only' : 'Mixed' }}</strong>
              </div>
              <div>
                <span>Tool Risk</span>
                <strong>{{ latestPlanRiskLine }}</strong>
              </div>
            </div>
            <div class="planner-source-line">
              <AiTag v-if="latestPlanIsCloudSandbox" tone="warning">Cloud 只读 Sandbox（非生产）</AiTag>
              <AiTag v-if="latestPlanIsCloudSandboxControlled" tone="warning">SandboxControlledTrial</AiTag>
              <AiTag v-if="!latestPlanIsCloudSandbox" tone="success">{{ latestPlanDataSource?.sourceLabel || 'AI 独立模拟业务库' }}</AiTag>
              <span>{{ latestPlanIsCloudSandbox ? latestCloudSandboxIntent?.endpointCodes?.join(' / ') || 'CloudReadonlySandbox' : latestPlanDataSource?.name || 'aicopilot_sim_business' }}</span>
            </div>
            <div v-if="latestCloudSandboxIntent" class="planner-intent-grid">
              <div>
                <span>Intent</span>
                <strong>{{ latestCloudSandboxIntent.analysisType || '-' }}</strong>
              </div>
              <div>
                <span>Max Rows</span>
                <strong>{{ latestCloudSandboxIntent.maxRows ?? '-' }}</strong>
              </div>
              <div>
                <span>Tool Approval</span>
                <strong>{{ latestCloudSandboxIntent.requiresToolApproval ? 'Required' : 'Not required' }}</strong>
              </div>
              <div>
                <span>Final Approval</span>
                <strong>{{ latestCloudSandboxIntent.requiresFinalApproval ? 'Required' : 'Not required' }}</strong>
              </div>
            </div>
            <div v-if="latestPlan?.plannerFallbackReason" class="planner-warning">
              <TriangleAlert :size="15" />
              <span>{{ latestPlan.plannerFallbackReason }}</span>
            </div>
            <div v-if="latestPlanForcedSteps.length" class="planner-chip-row">
              <span v-for="code in latestPlanForcedSteps" :key="code">{{ code }}</span>
            </div>
            <div v-if="latestPlan?.toolApprovalCheckpoints?.length" class="planner-chip-row">
              <span v-for="code in latestPlan.toolApprovalCheckpoints" :key="code">{{ code }}</span>
            </div>
          </div>
          <div v-if="blockedStep" class="blocked-step">
            <span>当前阻塞</span>
            <strong>{{ blockedStep.title }}</strong>
          </div>
          <textarea v-model="agentGoal" class="agent-goal" rows="4" placeholder="输入 Agent 任务目标，或复用当前问题生成计划" />
          <div class="two-actions">
            <button type="button" :disabled="!canCreatePlan || store.isAgentBusy" @click="createAgentPlan">
              <ListChecks :size="17" />
              生成计划
            </button>
            <button type="button" :disabled="!canRunTask || store.isAgentBusy" @click="runLatestTask">
              <Play :size="17" />
              运行任务
            </button>
          </div>
          <button class="wide-action" type="button" :disabled="!canContinueTask || store.isAgentBusy" @click="continueLatestTask">
            <RefreshCw :size="17" />
            继续任务
          </button>
          <div v-if="store.agentErrorMessage" class="canvas-error" role="alert">
            <TriangleAlert :size="18" />
            {{ store.agentErrorMessage }}
          </div>
        </section>

        <section v-else-if="uiLayoutStore.agentWorkbenchTab === 'steps'" class="tab-stack">
          <div class="section-title">
            <strong>{{ latestTask?.title || '尚未生成 Agent 任务' }}</strong>
            <AiTag :tone="statusTone(taskStatus.type)">{{ taskStatus.label }}</AiTag>
          </div>
          <div v-if="latestTask?.lastFailureReason" class="canvas-error">
            <TriangleAlert :size="18" />
            {{ latestTask.lastFailureReason }}
          </div>
          <div v-if="taskSteps.length" class="step-list">
            <div v-for="step in taskSteps" :key="step.id" class="step-row">
              <i>{{ step.stepIndex }}</i>
              <div>
                <strong>{{ step.title }}</strong>
                <span v-if="step.toolCode">{{ step.toolCode }}</span>
                <span v-if="step.errorMessage" class="danger-text">{{ step.errorMessage }}</span>
              </div>
              <AiTag :tone="step.status === 'Completed' ? 'success' : step.requiresApproval ? 'warning' : 'neutral'">
                {{ step.status }}
              </AiTag>
            </div>
          </div>
          <div v-else class="panel-empty">暂无步骤</div>
        </section>

        <section v-else-if="uiLayoutStore.agentWorkbenchTab === 'approvals'" class="tab-stack">
          <template v-if="approvalGroups.length">
            <div v-for="group in approvalGroups" :key="group.label" class="approval-group">
              <div class="group-title">{{ group.label }}</div>
              <div v-for="approval in group.approvals" :key="approval.id" class="approval-row">
                <div>
                  <strong>{{ approval.targetName }}</strong>
                  <span>{{ approval.riskLevel }} · {{ approval.reason || '等待确认' }}</span>
                </div>
                <div class="approval-actions">
                  <button type="button" :disabled="store.isAgentBusy" @click="approveAgentApproval(approval.id)">
                    <Check :size="16" />
                  </button>
                  <button type="button" :disabled="store.isAgentBusy" @click="rejectAgentApproval(approval.id)">
                    <X :size="16" />
                  </button>
                </div>
              </div>
            </div>
          </template>
          <div v-else class="panel-empty">暂无待审批项</div>
        </section>

        <section v-else-if="uiLayoutStore.agentWorkbenchTab === 'artifacts'" class="tab-stack">
          <div class="section-title">
            <strong>{{ workspaceStatus.label }}</strong>
            <span>{{ store.currentWorkspace?.workspaceCode || '未创建工作区' }}</span>
          </div>
          <div class="artifact-workspace-summary">
            <div>
              <span>草稿区</span>
              <strong>{{ draftArtifacts.length }}</strong>
            </div>
            <div>
              <span>final 区</span>
              <strong>{{ finalArtifacts.length }}</strong>
            </div>
            <div>
              <span>版本/锁定</span>
              <strong>{{ currentArtifactPreview?.artifactStatus || store.currentWorkspace?.status || '-' }}</strong>
            </div>
          </div>
          <div v-if="chartBars.length" class="chart-preview">
            <div class="chart-preview-head">
              <span>图表预览</span>
              <small>{{ store.chartPreview?.sourceMode || store.chartPreview?.source || 'workspace' }}</small>
            </div>
            <div v-for="bar in chartBars" :key="bar.label" class="chart-bar-row">
              <span>{{ bar.label }}</span>
              <div><i :style="{ width: bar.width }" /></div>
              <strong>{{ bar.value }}</strong>
            </div>
          </div>
          <template v-if="artifactGroups.length">
            <div v-for="group in artifactGroups" :key="group.label" class="artifact-group">
              <div class="group-title">{{ group.label }}</div>
              <div v-for="artifact in group.artifacts" :key="artifact.id" class="artifact-row">
                <div>
                  <strong>{{ artifact.name }}</strong>
                  <span>v{{ artifact.artifactVersion || artifact.version }} · {{ artifact.artifactStatus || artifact.status }} · {{ artifact.approvalStatus || '-' }}</span>
                  <span class="artifact-source-line">
                    {{ artifact.sourceMode || 'UnknownSource' }}
                    <template v-if="artifact.boundary"> · {{ artifact.boundary }}</template>
                    <template v-if="artifact.sourceLabel"> · {{ artifact.sourceLabel }}</template>
                  </span>
                  <span class="artifact-source-line">
                    hash {{ artifact.queryHash || artifact.resultHash || '-' }} · rows {{ artifact.rowCount ?? 0 }} · truncated {{ artifact.isTruncated ? 'true' : 'false' }}
                  </span>
                </div>
                <div class="artifact-actions">
                  <button type="button" aria-label="预览产物" @click="previewArtifact(artifact.id)">
                    <Eye :size="16" />
                  </button>
                  <button type="button" aria-label="下载产物" @click="downloadArtifact(artifact.id)">
                    <Download :size="16" />
                  </button>
                </div>
              </div>
            </div>
          </template>
          <div v-else class="panel-empty">暂无产物</div>
          <div v-if="currentArtifactPreview" class="artifact-preview-panel">
            <div class="section-title">
              <strong>{{ currentArtifactPreview.name }}</strong>
              <span>{{ currentArtifactPreview.previewKind }} · v{{ currentArtifactPreview.artifactVersion }}</span>
            </div>
            <div class="artifact-preview-meta">
              <AiTag :tone="currentArtifactPreview.isSandbox ? 'warning' : currentArtifactPreview.isSimulation ? 'success' : 'neutral'">
                {{ currentArtifactPreview.sourceMode || 'UnknownSource' }}
              </AiTag>
              <span>{{ currentArtifactPreview.sourceLabel || '未标记来源' }}</span>
              <span>hash {{ currentArtifactPreview.queryHash || currentArtifactPreview.resultHash || '-' }}</span>
              <span>rows {{ currentArtifactPreview.rowCount }} / truncated {{ currentArtifactPreview.isTruncated ? 'true' : 'false' }}</span>
            </div>
            <pre v-if="currentArtifactPreview.content" class="artifact-preview-content">{{ currentArtifactPreview.content.slice(0, 1600) }}</pre>
            <div v-else-if="currentArtifactPreview.rows?.length" class="artifact-preview-table">
              <div class="artifact-preview-table-head">
                <span v-for="column in currentArtifactPreview.columns" :key="column">{{ column }}</span>
              </div>
              <div v-for="(row, index) in currentArtifactPreview.rows.slice(0, 5)" :key="index" class="artifact-preview-table-row">
                <span v-for="column in currentArtifactPreview.columns" :key="column">{{ row[column] ?? '-' }}</span>
              </div>
            </div>
            <div v-else class="artifact-preview-metadata">
              <span v-for="(value, key) in currentArtifactPreview.metadata" :key="key">
                {{ key }}={{ value || '-' }}
              </span>
            </div>
          </div>
          <button class="wide-action" type="button" :disabled="!canFinalizeWorkspace || store.isAgentBusy" @click="finalizeCurrentWorkspace">
            <FolderOpen :size="17" />
            确认正式输出
          </button>
          <button class="wide-action muted" type="button" :disabled="!canSubmitFinalReview || store.isAgentBusy" @click="submitFinalReview">
            提交最终审批
          </button>
        </section>

        <section v-else-if="uiLayoutStore.agentWorkbenchTab === 'audit'" class="tab-stack">
          <button class="wide-action" type="button" :disabled="!latestTask || store.isAgentBusy" @click="refreshAuditSummary">
            <RefreshCw :size="17" />
            刷新审计摘要
          </button>
          <div v-if="auditSummary.length" class="audit-list">
            <div v-for="item in auditSummary" :key="item.id" class="audit-row">
              <div>
                <strong>{{ item.actionCode }}</strong>
                <span>{{ item.summary }}</span>
                <span v-if="item.metadata?.toolCode || item.metadata?.providerKind || item.metadata?.resultHash" class="audit-meta-line">
                  {{ item.metadata?.toolCode || 'tool' }}
                  · {{ item.metadata?.providerKind || item.metadata?.providerType || 'provider' }}
                  · {{ item.metadata?.isMock === 'True' || item.metadata?.isMock === 'true' ? 'Mock' : 'Runtime' }}
                  · hash {{ item.metadata?.resultHash || '-' }}
                </span>
              </div>
              <AiTag :tone="item.result === 'Succeeded' ? 'success' : 'danger'">{{ item.result }}</AiTag>
            </div>
          </div>
          <div v-else class="panel-empty">暂无审计记录</div>
        </section>

        <section v-else-if="uiLayoutStore.agentWorkbenchTab === 'trial'" class="tab-stack">
          <div class="trial-ops-card">
            <div class="section-title">
              <strong>{{ trialCampaign?.name || '内部试用运营台账' }}</strong>
              <AiTag :tone="trialCampaign?.readinessStatus === 'ReadyForP11Planning' ? 'success' : trialCampaign?.readinessStatus === 'Blocked' ? 'danger' : 'warning'">
                {{ trialCampaign?.readinessStatus || 'NotEvaluated' }}
              </AiTag>
            </div>
            <div class="trial-source-line">
              <span v-for="mode in trialCampaign?.allowedSourceModes || ['SimulationBusiness', 'CloudReadonlySandbox']" :key="mode">
                {{ mode }}
              </span>
            </div>
            <div class="trial-metric-grid">
              <div>
                <span>场景</span>
                <strong>{{ trialSummary?.scenarioRunCount ?? 0 }}</strong>
              </div>
              <div>
                <span>final</span>
                <strong>{{ trialSummary?.finalArtifactCount ?? 0 }}</strong>
              </div>
              <div>
                <span>未关风险</span>
                <strong>{{ trialSummary?.unresolvedRiskCount ?? 0 }}</strong>
              </div>
            </div>
            <div class="two-actions">
              <button type="button" :disabled="store.isLoadingTrialOperations" @click="createTrialCampaign">
                <ListChecks :size="16" />
                新建台账
              </button>
              <button type="button" :disabled="!latestTask || store.isLoadingTrialOperations" @click="attachLatestTaskToTrialCampaign">
                <FolderOpen :size="16" />
                挂载当前任务
              </button>
            </div>
          </div>

          <div class="trial-ops-card">
            <div class="section-title">
              <strong>P10 Pilot Planning 闸门</strong>
              <span>{{ trialReadiness?.generatedAt || '未评估' }}</span>
            </div>
            <div class="two-actions">
              <button type="button" :disabled="!trialCampaign || store.isLoadingTrialOperations" @click="runPilotReadinessEvaluation">
                <ShieldCheck :size="16" />
                运行评估
              </button>
              <button type="button" :disabled="!trialCampaign || store.isLoadingTrialOperations" @click="generateTrialEvidencePackage">
                <History :size="16" />
                生成证据包
              </button>
            </div>
            <div v-if="trialReadiness?.checks.length" class="trial-check-list">
              <div v-for="check in trialReadiness.checks" :key="check.code" class="trial-check-row">
                <div>
                  <strong>{{ check.label }}</strong>
                  <span>{{ check.message }}</span>
                </div>
                <AiTag :tone="check.status === 'Passed' ? 'success' : check.isBlocking ? 'danger' : 'warning'">
                  {{ check.status }}
                </AiTag>
              </div>
            </div>
            <div v-else class="panel-empty">尚未生成准入评估</div>
          </div>

          <div class="trial-ops-card" data-testid="p11-pilot-readiness-panel">
            <div class="section-title">
              <strong>P11 Pilot 准入演练</strong>
              <AiTag :tone="cloudReadonlyPilotReadiness?.status === 'RehearsalPassed' ? 'success' : cloudReadonlyPilotReadiness?.status === 'Blocked' ? 'danger' : 'warning'">
                {{ cloudReadonlyPilotReadiness?.status || 'NotConfigured' }}
              </AiTag>
            </div>
            <div class="trial-source-line">
              <span data-testid="p11-no-production-read">未启用生产读取</span>
              <span data-testid="p11-no-production-data">未接入真实生产数据</span>
              <span>query_cloud_data_readonly closed</span>
            </div>
            <div class="trial-metric-grid">
              <div>
                <span>contract</span>
                <strong>{{ cloudReadonlyPilotReadiness?.contractCheckSummary.passed ?? 0 }}/{{ cloudReadonlyPilotReadiness?.contractCheckSummary.total ?? 0 }}</strong>
              </div>
              <div>
                <span>blocked</span>
                <strong>{{ cloudReadonlyPilotReadiness?.contractCheckSummary.blockedByPolicy ?? 0 }}</strong>
              </div>
              <div>
                <span>approval</span>
                <strong>{{ cloudReadonlyPilotReadiness?.approvalRehearsalStatus || 'NotRun' }}</strong>
              </div>
            </div>
            <div class="two-actions">
              <button type="button" :disabled="!trialCampaign || store.isLoadingTrialOperations" @click="createCloudReadonlyPilotConfigPackage">
                <ListChecks :size="16" />
                配置包
              </button>
              <button type="button" :disabled="!trialCampaign || store.isLoadingTrialOperations" @click="runCloudReadonlyPilotGateEvaluation">
                <ShieldCheck :size="16" />
                Gate
              </button>
            </div>
            <div class="two-actions">
              <button type="button" :disabled="!cloudReadonlyPilotConfigPackage || store.isLoadingTrialOperations" @click="runCloudReadonlyPilotApprovalRehearsal">
                <Check :size="16" />
                审批演练
              </button>
              <button type="button" :disabled="!cloudReadonlyPilotConfigPackage || store.isLoadingTrialOperations" @click="runCloudReadonlyPilotContractRehearsal">
                <RefreshCw :size="16" />
                fake contract
              </button>
            </div>
            <div v-if="cloudReadonlyPilotConfigPackage" class="trial-source-line" data-testid="p11-config-package">
              <span>{{ cloudReadonlyPilotConfigPackage.packageId }}</span>
              <span>{{ cloudReadonlyPilotConfigPackage.allowedEndpointCodes.join(' / ') }}</span>
              <span>maxRows {{ cloudReadonlyPilotConfigPackage.maxRows }}</span>
            </div>
            <div v-if="pilotApprovalRehearsal?.steps.length" class="trial-check-list" data-testid="p11-approval-rehearsal">
              <div v-for="step in pilotApprovalRehearsal.steps" :key="step.code" class="trial-check-row">
                <div>
                  <strong>{{ step.label }}</strong>
                  <span>{{ step.auditRef }}</span>
                </div>
                <AiTag :tone="step.status === 'Passed' ? 'success' : step.isBlocking ? 'danger' : 'warning'">
                  {{ step.status }}
                </AiTag>
              </div>
            </div>
            <div v-if="pilotContractRehearsal?.checks.length" class="trial-check-list" data-testid="p11-contract-rehearsal">
              <div v-for="check in pilotContractRehearsal.checks" :key="check.endpointCode" class="trial-check-row">
                <div>
                  <strong>{{ check.endpointCode }}</strong>
                  <span>{{ check.status }} · rows {{ check.rowCount }} · hash {{ check.resultHash || '-' }}</span>
                </div>
                <AiTag :tone="check.status === 'Passed' ? 'success' : check.status === 'BlockedByPolicy' ? 'warning' : 'danger'">
                  {{ check.policyStatus }}
                </AiTag>
              </div>
            </div>
            <div v-if="cloudReadonlyPilotReadiness?.blockers.length" class="trial-check-list">
              <div v-for="blocker in cloudReadonlyPilotReadiness.blockers" :key="blocker" class="trial-check-row">
                <div>
                  <strong>阻塞项</strong>
                  <span>{{ blocker }}</span>
                </div>
                <AiTag tone="danger">Blocked</AiTag>
              </div>
            </div>
          </div>

          <div class="trial-ops-card" data-testid="p12-production-pilot-panel">
            <div class="section-title">
              <strong>P12 生产只读 Pilot</strong>
              <AiTag :tone="cloudReadonlyProductionPilotStatus?.status === 'Ready' ? 'success' : cloudReadonlyProductionPilotStatus?.status === 'Blocked' ? 'danger' : 'warning'">
                {{ cloudReadonlyProductionPilotStatus?.status || 'Disabled' }}
              </AiTag>
            </div>
            <div class="trial-source-line">
              <span data-testid="p12-fixed-template-marker">固定模板</span>
              <span data-testid="p12-production-readonly-marker">CloudReadonlyProductionPilot</span>
              <span data-testid="p12-gated-marker">Pilot Window + Approval required</span>
            </div>
            <div class="trial-metric-grid">
              <div>
                <span>window</span>
                <strong>{{ cloudReadonlyProductionPilotStatus?.pilotWindowId || cloudReadonlyProductionPilotWindow?.windowId || '-' }}</strong>
              </div>
              <div>
                <span>approval</span>
                <strong>{{ cloudReadonlyProductionPilotStatus?.approvalStatus || 'Required' }}</strong>
              </div>
              <div>
                <span>tool</span>
                <strong>{{ cloudReadonlyProductionPilotStatus?.toolExecutable ? 'Executable' : 'Closed' }}</strong>
              </div>
            </div>
            <div class="trial-source-line" data-testid="p12-allowlist">
              <span>{{ cloudReadonlyProductionPilotStatus?.allowedEndpointCodes?.join(' / ') || 'devices / capacity_summary / device_logs / pass_station_records' }}</span>
            </div>
            <div class="two-actions">
              <button type="button" :disabled="store.isLoadingTrialOperations" @click="createCloudReadonlyProductionPilotWindow">
                <ListChecks :size="16" />
                Window
              </button>
              <button type="button" :disabled="!cloudReadonlyProductionPilotWindow && !cloudReadonlyProductionPilotStatus?.pilotWindowId || store.isLoadingTrialOperations" @click="approveCloudReadonlyProductionPilotWindow">
                <Check :size="16" />
                Approve
              </button>
            </div>
            <div class="two-actions">
              <button type="button" :disabled="store.isLoadingTrialOperations" @click="runCloudReadonlyProductionPilotGate">
                <ShieldCheck :size="16" />
                Gate
              </button>
              <button type="button" :disabled="cloudReadonlyProductionPilotStatus?.status !== 'Ready' || store.isLoadingTrialOperations" @click="runCloudReadonlyProductionPilotScenario">
                <RefreshCw :size="16" />
                Fixed scenario
              </button>
            </div>
            <div v-if="cloudReadonlyProductionPilotRun" class="trial-check-list" data-testid="p12-production-pilot-run">
              <div class="trial-check-row">
                <div>
                  <strong>{{ cloudReadonlyProductionPilotRun.scenarioTitle }}</strong>
                  <span>{{ cloudReadonlyProductionPilotRun.queryResult.endpointCode }} · {{ cloudReadonlyProductionPilotRun.queryResult.rowCount }} rows · {{ cloudReadonlyProductionPilotRun.queryResult.resultHash }}</span>
                  <span>{{ cloudReadonlyProductionPilotRun.queryResult.sourceMode }} · {{ cloudReadonlyProductionPilotRun.queryResult.boundary }}</span>
                </div>
                <AiTag tone="success">{{ cloudReadonlyProductionPilotRun.status }}</AiTag>
              </div>
            </div>
            <div v-if="cloudReadonlyProductionPilotStatus?.blockers.length" class="trial-check-list">
              <div v-for="blocker in cloudReadonlyProductionPilotStatus.blockers" :key="blocker" class="trial-check-row">
                <div>
                  <strong>Blocked</strong>
                  <span>{{ blocker }}</span>
                </div>
                <AiTag tone="danger">Blocked</AiTag>
              </div>
            </div>
          </div>

          <div class="trial-ops-card" data-testid="p13-production-controlled-panel">
            <div class="section-title">
              <strong>P13 Controlled Production Pilot</strong>
              <AiTag :tone="cloudReadonlyProductionControlledStatus?.status === 'Ready' ? 'success' : cloudReadonlyProductionControlledStatus?.status === 'Blocked' ? 'danger' : 'warning'">
                {{ cloudReadonlyProductionControlledStatus?.status || 'Disabled' }}
              </AiTag>
            </div>
            <div class="trial-source-line">
              <span data-testid="p13-controlled-marker">CloudReadonlyProductionControlledPilot</span>
              <span data-testid="p13-boundary-marker">ProductionControlledPilot</span>
              <span>query_cloud_data_readonly closed</span>
            </div>
            <div class="trial-metric-grid">
              <div>
                <span>P12 gate</span>
                <strong>{{ cloudReadonlyProductionControlledStatus?.p12GateStatus || 'Required' }}</strong>
              </div>
              <div>
                <span>free goal</span>
                <strong>{{ cloudReadonlyProductionControlledStatus?.freeGoalEnabled ? 'Enabled' : 'Closed' }}</strong>
              </div>
              <div>
                <span>tool</span>
                <strong>{{ cloudReadonlyProductionControlledStatus?.toolExecutable ? 'Executable' : 'Closed' }}</strong>
              </div>
            </div>
            <div class="trial-source-line" data-testid="p13-allowlist">
              <span>{{ cloudReadonlyProductionControlledStatus?.allowedEndpointCodes?.join(' / ') || 'devices / capacity_summary / device_logs / pass_station_records' }}</span>
            </div>
            <textarea
              v-model="cloudProductionControlledGoal"
              class="agent-goal compact"
              rows="3"
              placeholder="输入生产只读受控自由目标，例如：分析最近一天设备异常"
            />
            <div class="two-actions">
              <button
                type="button"
                :disabled="store.isAgentBusy || !(cloudProductionControlledGoal.trim() || agentGoal.trim() || inputValue.trim())"
                @click="createCloudReadonlyProductionControlledPlan"
              >
                <ShieldCheck :size="16" />
                Intent + Plan
              </button>
              <button
                type="button"
                :disabled="!cloudProductionControlledIntent || store.isLoadingTrialOperations"
                @click="runCloudReadonlyProductionControlledPilot"
              >
                <RefreshCw :size="16" />
                Direct smoke
              </button>
            </div>
            <div v-if="cloudProductionControlledIntent" class="trial-check-list" data-testid="p13-production-controlled-intent">
              <div class="trial-check-row">
                <div>
                  <strong>{{ cloudProductionControlledIntent.analysisType }}</strong>
                  <span>{{ cloudProductionControlledIntent.endpointCodes?.join(' / ') }} · maxRows {{ cloudProductionControlledIntent.maxRows }} · {{ cloudProductionControlledIntent.intentId }}</span>
                  <span>ToolApproval {{ cloudProductionControlledIntent.requiresToolApproval ? 'required' : 'not-required' }} · FinalApproval {{ cloudProductionControlledIntent.requiresFinalApproval ? 'required' : 'not-required' }}</span>
                </div>
                <AiTag tone="warning">Intent</AiTag>
              </div>
            </div>
            <div v-if="cloudReadonlyProductionControlledRun" class="trial-check-list" data-testid="p13-production-controlled-run">
              <div class="trial-check-row">
                <div>
                  <strong>{{ cloudReadonlyProductionControlledRun.analysisType }}</strong>
                  <span>{{ cloudReadonlyProductionControlledRun.queryResult.endpointCode }} · {{ cloudReadonlyProductionControlledRun.queryResult.rowCount }} rows · {{ cloudReadonlyProductionControlledRun.queryResult.resultHash }}</span>
                  <span>{{ cloudReadonlyProductionControlledRun.queryResult.sourceMode }} · {{ cloudReadonlyProductionControlledRun.queryResult.boundary }}</span>
                </div>
                <AiTag tone="success">{{ cloudReadonlyProductionControlledRun.status }}</AiTag>
              </div>
            </div>
            <div v-if="cloudReadonlyProductionControlledStatus?.blockers.length" class="trial-check-list">
              <div v-for="blocker in cloudReadonlyProductionControlledStatus.blockers" :key="blocker" class="trial-check-row">
                <div>
                  <strong>Blocked</strong>
                  <span>{{ blocker }}</span>
                </div>
                <AiTag tone="danger">Blocked</AiTag>
              </div>
            </div>
          </div>

          <div class="trial-ops-card" data-testid="p14-production-operations-panel">
            <div class="section-title">
              <strong>P14 Production Pilot Operations</strong>
              <AiTag :tone="cloudReadonlyProductionOperationsStatus?.status === 'ReadyForP15Planning' ? 'success' : cloudReadonlyProductionOperationsStatus?.status === 'Blocked' || cloudReadonlyProductionOperationsStatus?.emergencyStopActive ? 'danger' : 'warning'">
                {{ cloudReadonlyProductionOperationsStatus?.status || 'CollectingEvidence' }}
              </AiTag>
            </div>
            <div class="trial-source-line">
              <span data-testid="p14-non-ga-marker">Production readonly Pilot, not full production rollout</span>
              <span>query_cloud_data_readonly closed</span>
            </div>
            <div class="trial-metric-grid">
              <div>
                <span>P12</span>
                <strong>{{ cloudReadonlyProductionOperationsStatus?.p12PilotStatus || cloudReadonlyProductionPilotStatus?.status || 'Required' }}</strong>
              </div>
              <div>
                <span>P13</span>
                <strong>{{ cloudReadonlyProductionOperationsStatus?.p13ControlledPilotStatus || cloudReadonlyProductionControlledStatus?.status || 'Required' }}</strong>
              </div>
              <div>
                <span>emergency</span>
                <strong data-testid="p14-emergency-stop-state">{{ cloudReadonlyProductionOperationsStatus?.emergencyStopActive ? 'Active' : 'Clear' }}</strong>
              </div>
              <div>
                <span>runs</span>
                <strong>{{ cloudReadonlyProductionOperationsStatus?.runMetrics.totalRuns ?? productionPilotRunLedger.length }}</strong>
              </div>
              <div>
                <span>rows</span>
                <strong>{{ cloudReadonlyProductionOperationsStatus?.runMetrics.totalRows ?? 0 }}</strong>
              </div>
              <div>
                <span>incidents</span>
                <strong>{{ cloudReadonlyProductionOperationsStatus?.runMetrics.openIncidentCount ?? 0 }}</strong>
              </div>
              <div>
                <span>persisted</span>
                <strong>{{ cloudReadonlyProductionOperationsStatus?.operationsStorePersisted ? 'Yes' : 'No' }}</strong>
              </div>
              <div>
                <span>P12 evidence</span>
                <strong>{{ cloudReadonlyProductionOperationsStatus?.hasP12CompletedRun ? 'Done' : 'Missing' }}</strong>
              </div>
              <div>
                <span>P13 evidence</span>
                <strong>{{ cloudReadonlyProductionOperationsStatus?.hasP13CompletedRun ? 'Done' : 'Missing' }}</strong>
              </div>
            </div>
            <div class="two-actions">
              <button type="button" :disabled="store.isLoadingTrialOperations" @click="activateProductionPilotEmergencyStop">
                <TriangleAlert :size="16" />
                Emergency stop
              </button>
              <button type="button" :disabled="store.isLoadingTrialOperations" @click="clearProductionPilotEmergencyStop">
                <Check :size="16" />
                Clear stop
              </button>
            </div>
            <div class="two-actions">
              <button type="button" :disabled="store.isLoadingTrialOperations" @click="createProductionPilotIncident">
                <ListChecks :size="16" />
                Open incident
              </button>
              <button type="button" :disabled="store.isLoadingTrialOperations" @click="runProductionPilotGaReadinessEvaluation">
                <ShieldCheck :size="16" />
                P15 readiness
              </button>
            </div>
            <div v-if="productionPilotRunLedger.length" class="trial-check-list" data-testid="p14-run-ledger">
              <div v-for="run in productionPilotRunLedger.slice(0, 3)" :key="run.runId" class="trial-check-row">
                <div>
                  <strong>{{ run.endpointCode }} · {{ run.sourceMode }}</strong>
                  <span>{{ run.boundary }} · {{ run.rowCount }} rows · {{ run.resultHash }}</span>
                  <span>{{ run.approvalStatus }} · {{ run.status }}</span>
                </div>
                <AiTag :tone="run.status === 'Completed' ? 'success' : run.status === 'Failed' ? 'danger' : 'warning'">
                  {{ run.status }}
                </AiTag>
              </div>
            </div>
            <div v-if="productionPilotGaReadiness" class="trial-check-list" data-testid="p14-ga-readiness">
              <div class="trial-check-row">
                <div>
                  <strong>P15 GA readiness</strong>
                  <span>{{ productionPilotGaReadiness.status }} · blockers {{ productionPilotGaReadiness.blockers.length }}</span>
                </div>
                <AiTag :tone="productionPilotGaReadiness.status === 'ReadyForP15Planning' ? 'success' : 'danger'">
                  {{ productionPilotGaReadiness.status }}
                </AiTag>
              </div>
            </div>
            <div v-if="cloudReadonlyProductionOperationsStatus?.blockers.length" class="trial-check-list">
              <div v-for="blocker in cloudReadonlyProductionOperationsStatus.blockers" :key="blocker" class="trial-check-row">
                <div>
                  <strong>Blocked</strong>
                  <span>{{ blocker }}</span>
                </div>
                <AiTag tone="danger">Blocked</AiTag>
              </div>
            </div>
          </div>

          <div class="trial-ops-card" data-testid="p15-planning-gate-panel">
            <div class="section-title">
              <strong>P15 Pilot Planning Gate</strong>
              <AiTag tone="warning">PlanningOnly</AiTag>
            </div>
            <div class="trial-source-line">
              <span data-testid="p15-planning-marker">Planning and authorization gate, not real Pilot execution</span>
              <span data-testid="p15-not-ga-marker">Not GA</span>
              <span>query_cloud_data_readonly closed</span>
            </div>
            <div class="trial-metric-grid">
              <div>
                <span>users</span>
                <strong>5-10</strong>
              </div>
              <div>
                <span>range</span>
                <strong>7 days</strong>
              </div>
              <div>
                <span>maxRows</span>
                <strong>50</strong>
              </div>
              <div>
                <span>status</span>
                <strong data-testid="p15-blocked-status">ReadyForP16PlanningBlocked</strong>
              </div>
            </div>
            <div class="trial-source-line" data-testid="p15-allowlist">
              <span>devices</span>
              <span>capacity_summary</span>
              <span>device_logs</span>
              <span>pass_station_records</span>
            </div>
            <div class="trial-check-list" data-testid="p15-blocker-list">
              <div class="trial-check-row">
                <div>
                  <strong>P12/P13 persistence</strong>
                  <span>Window, intent, and run stores must be persisted before P16.</span>
                </div>
                <AiTag tone="danger">Blocker</AiTag>
              </div>
              <div class="trial-check-row">
                <div>
                  <strong>Artifact refs backfill</strong>
                  <span>Final artifact refs must automatically update ProductionPilotRunLedger.</span>
                </div>
                <AiTag tone="danger">Blocker</AiTag>
              </div>
              <div class="trial-check-row">
                <div>
                  <strong>Rows retention</strong>
                  <span>Rows masking, TTL, download, and artifact-use policy must be approved.</span>
                </div>
                <AiTag tone="danger">Blocker</AiTag>
              </div>
            </div>
          </div>

          <div v-if="trialCampaign?.scenarioRuns.length" class="trial-run-list">
            <div v-for="run in trialCampaign.scenarioRuns" :key="run.runId" class="trial-run-row">
              <div>
                <strong>{{ run.scenarioId }}</strong>
                <span>{{ run.sourceMode }} · {{ run.boundary || 'NonProduction' }} · {{ run.approvalStatus }}</span>
                <span>hash {{ run.queryHashes[0] || run.resultHashes[0] || '-' }}</span>
              </div>
              <AiTag :tone="run.status === 'Passed' ? 'success' : run.status === 'Blocked' || run.status === 'Failed' ? 'danger' : 'warning'">
                {{ run.status }}
              </AiTag>
            </div>
          </div>
          <div v-else class="panel-empty">暂无挂载的试用任务</div>

          <div v-if="trialEvidencePackage" class="trial-ops-card">
            <div class="section-title">
              <strong>证据包</strong>
              <span>{{ trialEvidencePackage.readinessStatus }}</span>
            </div>
            <div class="trial-metric-grid">
              <div v-for="metric in trialEvidencePackage.metrics.slice(0, 6)" :key="metric.code">
                <span>{{ metric.label }}</span>
                <strong>{{ metric.value }}</strong>
              </div>
            </div>
          </div>
        </section>

        <section v-else class="tab-stack">
          <div class="boundary-card">
            <ShieldCheck :size="24" />
            <strong>Cloud 只读边界</strong>
            <span>AICopilot 只做分析、解释、总结和建议，不写回 Cloud 主数据。</span>
          </div>
          <div class="boundary-list">
            <div>
              <span>现场确认</span>
              <AiTag :tone="statusTone(onsiteStatus.type)">{{ onsiteStatus.label }}</AiTag>
            </div>
            <div>
              <span>登录来源</span>
              <strong>{{ loginSource }}</strong>
            </div>
            <div v-if="cloudEmployeeNo">
              <span>Cloud 员工</span>
              <strong>{{ cloudEmployeeNo }}</strong>
            </div>
            <div>
              <span>会话审批</span>
              <strong>{{ approvalCount }}</strong>
            </div>
            <div>
              <span>图表组件</span>
              <strong>{{ widgetCount }}</strong>
            </div>
          </div>
          <button class="wide-action" type="button" @click="store.confirmOnsitePresence(30)">
            <ArrowUp :size="17" />
            确认在岗 30 分钟
          </button>
          <button class="wide-action muted" type="button" @click="store.clearOnsitePresence()">
            清除在岗声明
          </button>
        </section>
      </div>
    </aside>

    <div v-if="sessionDrawerVisible" class="mobile-overlay" @click.self="sessionDrawerVisible = false">
      <aside class="mobile-drawer left">
        <button class="drawer-close" type="button" aria-label="关闭会话" @click="sessionDrawerVisible = false">
          <X :size="18" />
        </button>
        <SessionList />
      </aside>
    </div>

    <div
      v-if="isMobile && uiLayoutStore.isAgentWorkbenchDrawerOpen"
      class="agent-mobile-backdrop"
      @click="uiLayoutStore.setAgentWorkbenchDrawerOpen(false)"
    />
  </div>
</template>

<style scoped>
.command-workbench {
  display: grid;
  grid-template-columns: minmax(260px, 300px) minmax(0, 1fr) minmax(330px, 380px);
  height: 100%;
  min-height: 0;
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.74);
  border-radius: 28px;
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-card);
}

.session-task-rail,
.agent-workbench-panel {
  min-width: 0;
  min-height: 0;
  overflow: hidden;
  background: color-mix(in srgb, var(--ai-surface) 90%, var(--ai-surface-soft));
}

.session-task-rail {
  display: grid;
  grid-template-rows: auto minmax(0, 1fr) auto;
  border-right: 1px solid var(--ai-border);
}

.rail-head,
.agent-panel-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 18px;
}

.rail-head div,
.agent-panel-head div,
.section-title {
  display: grid;
  gap: 3px;
  min-width: 0;
}

.rail-head span,
.agent-panel-head span,
.canvas-kicker {
  color: var(--ai-text-soft);
  font-size: 11px;
  font-weight: 850;
}

.rail-head strong,
.agent-panel-head strong {
  font-size: 18px;
  font-weight: 900;
}

.rail-head button,
.icon-button,
.approval-actions button,
.artifact-row button,
.drawer-close {
  display: grid;
  width: 38px;
  height: 38px;
  place-items: center;
  border: 1px solid var(--ai-border);
  border-radius: 999px;
  background: var(--ai-surface);
  color: var(--ai-text);
  cursor: pointer;
}

.sessions {
  min-height: 0;
}

.task-history {
  display: grid;
  gap: 12px;
  border-top: 1px solid var(--ai-border);
  padding: 16px;
}

.task-history header {
  display: flex;
  align-items: center;
  gap: 10px;
}

.task-history header div,
.task-history-list,
.tab-stack,
.metric-grid div,
.blocked-step,
.step-row div,
.approval-row div,
.artifact-row div,
.audit-row div {
  display: grid;
  gap: 4px;
  min-width: 0;
}

.task-history header span,
.rail-empty,
.panel-empty,
.section-title span,
.step-row span,
.approval-row span,
.artifact-row span,
.audit-row span,
.boundary-list span,
.uploaded-hint {
  color: var(--ai-text-muted);
  font-size: 12px;
}

.trial-scenario-panel {
  display: grid;
  gap: 12px;
}

.controlled-sandbox-panel {
  padding: 12px;
  border: 1px solid rgba(245, 158, 11, 0.28);
  border-radius: 14px;
  background: rgba(255, 251, 235, 0.72);
}

.trial-scenario-list {
  display: grid;
  gap: 8px;
}

.trial-scenario-card {
  display: grid;
  gap: 5px;
  padding: 10px 11px;
  border: 1px solid rgba(148, 163, 184, 0.28);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.82);
  color: inherit;
  text-align: left;
  cursor: pointer;
}

.trial-scenario-card.active,
.trial-scenario-card:hover {
  border-color: rgba(14, 165, 166, 0.7);
  background: rgba(240, 253, 250, 0.9);
}

.trial-scenario-card span {
  color: #0f766e;
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 0;
  text-transform: uppercase;
}

.trial-scenario-card strong {
  font-size: 13px;
  line-height: 1.25;
}

.trial-scenario-card small,
.trial-scenario-card em {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-style: normal;
  line-height: 1.35;
}

.planner-preview {
  display: grid;
  gap: 10px;
  padding: 12px;
  border: 1px solid rgba(14, 165, 166, 0.22);
  border-radius: 8px;
  background: rgba(240, 253, 250, 0.72);
}

.planner-preview-head,
.planner-source-line {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.planner-preview-head div,
.planner-preview-grid div {
  display: grid;
  gap: 3px;
}

.planner-preview span,
.planner-preview-grid span {
  color: var(--ai-text-muted);
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0;
}

.planner-preview strong {
  color: var(--ai-text);
  font-size: 13px;
  line-height: 1.3;
}

.planner-preview-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
}

.planner-intent-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
}

.planner-intent-grid div {
  display: grid;
  gap: 3px;
  min-width: 0;
  padding: 8px;
  border: 1px solid rgba(245, 158, 11, 0.24);
  border-radius: 8px;
  background: rgba(255, 251, 235, 0.72);
}

.planner-source-line {
  justify-content: flex-start;
  flex-wrap: wrap;
}

.planner-warning {
  display: flex;
  align-items: flex-start;
  gap: 7px;
  color: #92400e;
  font-size: 12px;
  line-height: 1.35;
}

.planner-chip-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.planner-chip-row span {
  padding: 4px 7px;
  border-radius: 999px;
  background: rgba(15, 118, 110, 0.1);
  color: #0f766e;
  font-size: 11px;
  font-weight: 800;
}

.task-history-list {
  max-height: 220px;
  overflow-y: auto;
}

.task-history-item {
  display: grid;
  gap: 4px;
  border: 1px solid var(--ai-border);
  border-radius: 16px;
  padding: 11px;
  background: var(--ai-surface);
  color: var(--ai-text);
  cursor: pointer;
  text-align: left;
}

.task-history-item.active {
  border-color: var(--ai-graphite);
  box-shadow: 0 0 0 4px rgba(63, 111, 115, 0.12);
}

.task-history-item strong,
.task-history-item span,
.step-row strong,
.approval-row strong,
.artifact-row strong,
.audit-row strong {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ai-canvas {
  display: grid;
  grid-template-rows: auto auto minmax(0, 1fr) auto;
  min-width: 0;
  min-height: 0;
  background: var(--ai-canvas);
  color: var(--ai-text);
}

.canvas-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  min-height: 78px;
  border-bottom: 1px solid var(--ai-border);
  padding: 16px 20px;
}

.title-zone,
.canvas-toolbar,
.composer-tools,
.composer-input-row,
.soft-action,
.tool-button,
.send-button,
.two-actions,
.wide-action,
.boundary-list div {
  display: flex;
  align-items: center;
}

.title-zone {
  gap: 12px;
  min-width: 0;
}

.canvas-kicker {
  margin: 0 0 4px;
  color: var(--ai-text-soft);
}

.canvas-header h1 {
  margin: 0;
  overflow: hidden;
  color: var(--ai-text);
  font-size: 23px;
  font-weight: 950;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.canvas-toolbar {
  gap: 9px;
  flex-wrap: wrap;
  justify-content: flex-end;
}

.model-selector {
  min-height: 40px;
  width: 230px;
  border: 1px solid var(--ai-border);
  border-radius: 999px;
  padding: 0 14px;
  outline: none;
  background: var(--ai-surface);
  color: var(--ai-text);
  font-weight: 800;
}

.model-selector option {
  color: var(--ai-graphite);
}

.soft-action,
.tool-button,
.wide-action {
  gap: 8px;
  min-height: 40px;
  border: 1px solid var(--ai-border);
  border-radius: 999px;
  padding: 0 13px;
  background: var(--ai-surface);
  color: var(--ai-text);
  font-weight: 850;
  cursor: pointer;
}

.soft-action:disabled,
.tool-button:disabled,
.wide-action:disabled,
.send-button:disabled {
  cursor: not-allowed;
  opacity: 0.48;
}

.canvas-status-strip {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 10px;
  border-bottom: 1px solid var(--ai-border);
  padding: 12px 20px;
}

.status-tile {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  min-height: 48px;
  border: 1px solid var(--ai-border);
  border-radius: 16px;
  padding: 8px 10px;
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-xs);
}

.status-tile span {
  overflow: hidden;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.message-viewport {
  min-height: 0;
  overflow-y: auto;
  padding: 22px;
  background:
    radial-gradient(circle at 70% 12%, rgba(200, 255, 61, 0.13), transparent 28%),
    var(--ai-canvas);
}

.message-list {
  display: grid;
  gap: 16px;
  max-width: 980px;
  margin: 0 auto;
}

.canvas-error {
  display: flex;
  align-items: center;
  gap: 8px;
  max-width: 980px;
  margin: 0 auto 14px;
  border: 1px solid rgba(255, 123, 111, 0.35);
  border-radius: 16px;
  padding: 12px 14px;
  background: #fff7ed;
  color: #9a3412;
  font-weight: 800;
}

.loading-lines {
  display: grid;
  gap: 12px;
  max-width: 980px;
  margin: 0 auto;
}

.loading-lines i {
  height: 18px;
  border-radius: 999px;
  background: linear-gradient(90deg, rgba(222, 219, 211, 0.55), rgba(255, 255, 255, 0.92), rgba(222, 219, 211, 0.55));
}

.empty-chat {
  display: grid;
  gap: 14px;
  max-width: 760px;
  margin: 42px auto;
  border: 1px solid var(--ai-border);
  border-radius: 28px;
  padding: 28px;
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-canvas);
}

.empty-chat h2 {
  margin: 0;
  font-size: 28px;
  font-weight: 950;
}

.empty-chat p {
  margin: 0;
  color: var(--ai-text-muted);
}

.suggestions {
  display: grid;
  gap: 9px;
}

.suggestions button {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  border: 1px solid var(--ai-border);
  border-radius: 18px;
  padding: 13px 14px;
  background: var(--ai-surface-soft);
  color: var(--ai-text);
  cursor: pointer;
  font-weight: 800;
  text-align: left;
}

.command-composer {
  display: grid;
  gap: 10px;
  border-top: 1px solid var(--ai-border);
  padding: 14px 18px 18px;
  background: rgba(251, 250, 247, 0.88);
  box-shadow: 0 -12px 30px rgba(70, 64, 55, 0.06);
}

.composer-tools {
  gap: 8px;
  flex-wrap: wrap;
}

.hidden-file {
  display: none;
}

.tool-button {
  min-height: 34px;
  color: var(--ai-text-muted);
}

.uploaded-hint {
  color: var(--ai-text-muted);
  font-weight: 800;
}

.composer-input-row {
  gap: 10px;
  min-height: 60px;
  border: 1px solid var(--ai-border);
  border-radius: 24px;
  padding: 10px 10px 10px 18px;
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-xs);
}

.composer-input-row:focus-within {
  border-color: rgba(63, 111, 115, 0.28);
  box-shadow: 0 0 0 4px rgba(63, 111, 115, 0.1);
}

.composer-input-row textarea {
  width: 100%;
  min-width: 0;
  max-height: 160px;
  resize: vertical;
  border: 0;
  outline: none;
  background: transparent;
  color: var(--ai-text);
  font: inherit;
  line-height: 1.5;
}

.composer-input-row textarea::placeholder {
  color: var(--ai-text-soft);
}

.send-button {
  justify-content: center;
  width: 46px;
  height: 46px;
  border: 0;
  border-radius: 999px;
  background: var(--ai-lime);
  color: var(--ai-graphite);
  cursor: pointer;
}

.agent-workbench-panel {
  display: grid;
  grid-template-rows: auto auto minmax(0, 1fr);
  border-left: 1px solid var(--ai-border);
  padding: 14px;
}

.agent-panel-head {
  padding: 4px 4px 14px;
}

.agent-tabs {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 7px;
  border-radius: 20px;
  padding: 6px;
  background: var(--ai-surface-soft);
}

.agent-tabs button {
  display: inline-flex;
  min-height: 38px;
  align-items: center;
  justify-content: center;
  gap: 5px;
  border: 0;
  border-radius: 15px;
  background: transparent;
  color: var(--ai-text-muted);
  cursor: pointer;
  font-weight: 850;
}

.agent-tabs button.active {
  background: var(--ai-surface);
  color: var(--ai-text);
  box-shadow: var(--ai-shadow-xs);
}

.agent-tabs span {
  border-radius: 999px;
  padding: 1px 6px;
  background: var(--ai-lime);
  color: var(--ai-graphite);
  font-size: 10px;
}

.agent-tab-content {
  min-height: 0;
  overflow-y: auto;
  padding-top: 14px;
}

.tab-stack {
  gap: 12px;
}

.metric-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
}

.metric-grid div,
.blocked-step,
.chart-preview,
.boundary-card,
.boundary-list {
  border: 1px solid var(--ai-border);
  border-radius: 18px;
  padding: 12px;
  background: var(--ai-surface);
}

.metric-grid span,
.blocked-step span {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.metric-grid strong {
  font-size: 24px;
  font-variant-numeric: tabular-nums;
}

.agent-goal {
  width: 100%;
  resize: vertical;
  border: 1px solid var(--ai-border);
  border-radius: 18px;
  padding: 12px;
  outline: none;
  background: var(--ai-surface);
  color: var(--ai-text);
  font: inherit;
}

.agent-goal:focus {
  border-color: rgba(63, 111, 115, 0.34);
  box-shadow: 0 0 0 4px var(--ai-focus);
}

.agent-goal.compact {
  min-height: 86px;
  border-radius: 12px;
}

.two-actions {
  gap: 8px;
}

.two-actions button,
.wide-action {
  justify-content: center;
  width: 100%;
  min-height: 42px;
  border-color: var(--ai-border);
  background: var(--ai-graphite);
  color: white;
}

.wide-action.muted {
  background: var(--ai-surface);
  color: var(--ai-text);
}

.step-list,
.approval-group,
.artifact-group,
.audit-list,
.boundary-list {
  display: grid;
  gap: 8px;
}

.step-row,
.approval-row,
.artifact-row,
.audit-row {
  display: grid;
  align-items: center;
  gap: 8px;
  border: 1px solid var(--ai-border);
  border-radius: 16px;
  padding: 10px;
  background: var(--ai-surface);
}

.step-row {
  grid-template-columns: 30px minmax(0, 1fr) auto;
}

.step-row i {
  display: grid;
  width: 28px;
  height: 28px;
  place-items: center;
  border-radius: 999px;
  background: var(--ai-graphite);
  color: var(--ai-lime);
  font-size: 12px;
  font-style: normal;
  font-weight: 900;
}

.approval-row,
.artifact-row,
.audit-row {
  grid-template-columns: minmax(0, 1fr) auto;
}

.approval-actions {
  display: flex;
  gap: 6px;
}

.group-title {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 850;
}

.panel-empty {
  display: grid;
  min-height: 110px;
  place-items: center;
  border: 1px dashed var(--ai-border);
  border-radius: 18px;
}

.chart-preview {
  display: grid;
  gap: 8px;
}

.artifact-workspace-summary {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
}

.artifact-workspace-summary div {
  display: grid;
  gap: 4px;
  min-width: 0;
  border: 1px solid var(--ai-border);
  border-radius: 14px;
  padding: 10px;
  background: var(--ai-surface);
}

.artifact-workspace-summary span,
.artifact-source-line,
.artifact-preview-meta,
.artifact-preview-metadata {
  color: var(--ai-text-muted);
  font-size: 12px;
}

.artifact-actions {
  display: flex;
  gap: 6px;
}

.artifact-preview-panel {
  display: grid;
  gap: 10px;
  border: 1px solid var(--ai-border);
  border-radius: 16px;
  padding: 12px;
  background: var(--ai-surface);
}

.artifact-preview-meta,
.artifact-preview-metadata {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 10px;
  align-items: center;
}

.artifact-preview-content {
  max-height: 260px;
  overflow: auto;
  border: 1px solid var(--ai-border);
  border-radius: 12px;
  padding: 10px;
  background: #0f172a;
  color: #e5eef6;
  font-size: 12px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-word;
}

.artifact-preview-table {
  display: grid;
  overflow-x: auto;
  border: 1px solid var(--ai-border);
  border-radius: 12px;
}

.artifact-preview-table-head,
.artifact-preview-table-row {
  display: grid;
  grid-auto-flow: column;
  grid-auto-columns: minmax(96px, 1fr);
}

.artifact-preview-table-head span,
.artifact-preview-table-row span {
  min-width: 0;
  overflow: hidden;
  padding: 8px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.artifact-preview-table-head {
  background: var(--ai-surface-soft);
  font-weight: 800;
}

.trial-ops-card,
.trial-run-row,
.trial-check-row {
  display: grid;
  gap: 10px;
  border: 1px solid var(--ai-border);
  border-radius: 16px;
  padding: 12px;
  background: var(--ai-surface);
}

.trial-source-line {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.trial-source-line span {
  border: 1px solid var(--ai-border);
  border-radius: 999px;
  padding: 4px 8px;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.trial-metric-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
}

.trial-metric-grid div {
  display: grid;
  gap: 4px;
  min-width: 0;
  border: 1px solid var(--ai-border);
  border-radius: 14px;
  padding: 10px;
  background: var(--ai-surface-soft);
}

.trial-metric-grid span,
.trial-run-row span,
.trial-check-row span {
  color: var(--ai-text-muted);
  font-size: 12px;
}

.trial-run-list,
.trial-check-list {
  display: grid;
  gap: 8px;
}

.trial-run-row,
.trial-check-row {
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: center;
}

.chart-preview-head,
.chart-bar-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 8px;
  align-items: center;
}

.chart-bar-row {
  grid-template-columns: 86px minmax(0, 1fr) auto;
}

.chart-bar-row div {
  height: 8px;
  overflow: hidden;
  border-radius: 999px;
  background: var(--ai-border);
}

.chart-bar-row i {
  display: block;
  height: 100%;
  border-radius: inherit;
  background: var(--ai-teal);
}

.boundary-card {
  display: grid;
  gap: 8px;
  background: var(--ai-graphite);
  color: white;
}

.boundary-card span {
  color: #cbd5e1;
}

.boundary-list div {
  justify-content: space-between;
  gap: 12px;
}

.danger-text {
  color: #b42318;
}

.mobile-overlay {
  position: fixed;
  inset: 0;
  z-index: 60;
  background: rgba(48, 83, 86, 0.34);
}

.mobile-drawer {
  position: absolute;
  top: 0;
  bottom: 0;
  width: min(360px, 88vw);
  overflow-y: auto;
  padding: 18px;
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-shell);
}

.mobile-drawer.left {
  left: 0;
}

.mobile-drawer.right {
  right: 0;
}

.drawer-close {
  margin-left: auto;
  margin-bottom: 14px;
}

.agent-panel-head .drawer-close {
  margin-bottom: 0;
}

.mobile-panel-note {
  border-radius: 18px;
  padding: 14px;
  background: var(--ai-surface-soft);
  color: var(--ai-text-muted);
  font-weight: 800;
}

.agent-mobile-backdrop {
  position: fixed;
  inset: 0;
  z-index: 70;
  background: rgba(48, 83, 86, 0.34);
}

@media (max-width: 1280px) {
  .command-workbench {
    grid-template-columns: 260px minmax(0, 1fr) 340px;
  }

  .canvas-status-strip {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}

@media (max-width: 1024px) {
  .command-workbench {
    grid-template-columns: minmax(0, 1fr);
  }

  .canvas-header {
    align-items: flex-start;
    flex-direction: column;
  }

  .canvas-toolbar {
    width: 100%;
    justify-content: flex-start;
  }

  .model-selector {
    width: min(260px, 100%);
  }

  .agent-workbench-panel {
    position: fixed;
    top: 0;
    right: 0;
    bottom: 0;
    z-index: 80;
    width: min(390px, 90vw);
    transform: translateX(110%);
    transition: transform 0.2s ease;
    box-shadow: var(--ai-shadow-shell);
  }

  .agent-workbench-panel.mobile-open {
    transform: translateX(0);
  }
}
</style>
