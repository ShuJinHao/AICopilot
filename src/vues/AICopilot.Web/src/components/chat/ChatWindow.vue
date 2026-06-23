<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  Check,
  ChevronRight,
  Download,
  Eye,
  FileUp,
  FolderOpen,
  ListChecks,
  MessageCircle,
  PanelLeftOpen,
  Play,
  Plus,
  RefreshCw,
  Send,
  ShieldCheck,
  SlidersHorizontal,
  Sparkles,
  TriangleAlert,
  Wrench,
  X
} from 'lucide-vue-next'
import AiTag from '@/components/ai/AiTag.vue'
import { useAgentWorkbench } from '@/composables/useAgentWorkbench'
import { useChatStore } from '@/stores/chatStore'
import { useUiLayoutStore } from '@/stores/uiLayoutStore'
import MessageItem from './MessageItem.vue'
import SessionList from './SessionList.vue'
import type { AgentPlannerToolSummary } from '@/types/app'
import type { AgentApprovalRequest } from '@/types/protocols'

type AgentPlanPreview = {
  plannerMode?: string
  plannerFallbackReason?: string | null
  plannerToolCatalogVersion?: number
  plannerAvailableToolCount?: number
  toolCatalogVersion?: number
  visibleToolCount?: number
  toolRiskSummary?: Record<string, number>
  approvalCheckpoints?: string[]
  forcedStepCodes?: string[]
  queryMode?: string | null
  skillCode?: string | null
  skillName?: string | null
  taskType?: string | null
  dataSourceSummaries?: Array<{
    name?: string
    sourceMode?: string
    sourceLabel?: string
  }>
  plannerSafetySummary?: {
    planSource?: string
    plannerMode?: string
    plannerModelSummary?: string | null
    plannerToolCatalogVersion?: number
    availableToolCount?: number
    toolRiskSummary?: Record<string, number>
    mockMcpOnly?: boolean
  }
}

type TagTone = 'success' | 'warning' | 'dark' | 'lime' | 'danger' | 'neutral' | 'teal' | 'blue'
type ComposerMode = 'plan' | 'chat'

const store = useChatStore()
const uiLayoutStore = useUiLayoutStore()

const inputValue = ref('')
const agentGoal = ref('')
const composerMode = ref<ComposerMode>('plan')
const composerOptionsOpen = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)
const scrollContainer = ref<HTMLElement | null>(null)
const isMobile = ref(typeof window !== 'undefined' ? window.innerWidth < 1024 : false)
const sessionDrawerVisible = ref(false)
const preserveScrollAnchor = ref(false)

const {
  latestTask,
  taskSteps,
  taskArtifacts,
  draftArtifacts,
  finalArtifacts,
  currentArtifactPreview,
  pendingAgentApprovals,
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
  canCreatePlan,
  canRunTask,
  canContinueTask,
  canSubmitFinalReview,
  canFinalizeWorkspace
} = useAgentWorkbench()

const currentTitle = computed(() => store.currentSession?.title || '新会话')
const isInputDisabled = computed(() => store.isStreaming || store.isWaitingForApproval)
const latestPlan = computed<AgentPlanPreview | null>(() => parseAgentPlan(latestTask.value?.planJson))
const latestPlanDataSource = computed(() => latestPlan.value?.dataSourceSummaries?.[0] ?? null)
const latestPlanToolCatalogVersion = computed(
  () => latestPlan.value?.toolCatalogVersion ?? latestPlan.value?.plannerToolCatalogVersion ?? latestPlan.value?.plannerSafetySummary?.plannerToolCatalogVersion
)
const latestPlanVisibleToolCount = computed(
  () => latestPlan.value?.visibleToolCount ?? latestPlan.value?.plannerAvailableToolCount ?? latestPlan.value?.plannerSafetySummary?.availableToolCount ?? 0
)
const latestPlanRiskSummary = computed(
  () => latestPlan.value?.toolRiskSummary ?? latestPlan.value?.plannerSafetySummary?.toolRiskSummary ?? {}
)
const latestPlanRiskLine = computed(() => {
  const entries = Object.entries(latestPlanRiskSummary.value)
  return entries.length ? entries.map(([risk, count]) => `${risk}:${count}`).join(' / ') : '无风险摘要'
})
const latestPlanSource = computed(() =>
  sourceModeLabel(
    latestPlan.value?.skillName ||
    latestPlan.value?.skillCode ||
    latestPlanDataSource.value?.sourceLabel ||
    latestPlan.value?.plannerSafetySummary?.planSource ||
    latestPlanDataSource.value?.sourceMode ||
    'FreeGoal'
  )
)
const latestPlanIsCloudReadonly = computed(() =>
  latestPlan.value?.queryMode?.includes('CloudReadonly') ||
  latestPlanDataSource.value?.sourceMode?.includes('CloudReadonly') ||
  false
)
const planTypeValue = computed({
  get: () => store.selectedSkillCode || 'auto',
  set: (value: string) => {
    store.selectSkill(value === 'auto' ? null : value)
  }
})
const selectedPlanTypeLabel = computed(() =>
  store.selectedSkill?.displayName || '自动识别'
)
const selectedPluginLine = computed(() => {
  if (store.selectedPluginTools.length > 0) {
    return `${store.selectedPluginTools.length} 个插件能力`
  }

  if (store.availablePluginTools.length > 0) {
    return '可选插件能力'
  }

  return '无可选插件'
})
const composerPrimaryLabel = computed(() => composerMode.value === 'plan' ? '生成计划' : '发送')
const composerPrimaryIcon = computed(() => composerMode.value === 'plan' ? ListChecks : Send)
const composerPlaceholder = computed(() => {
  if (store.isWaitingForApproval) {
    return '请先处理待审批请求'
  }

  return composerMode.value === 'plan'
    ? '输入目标，先生成可确认的计划'
    : '输入一个简单问题，直接回答'
})
const isComposerSubmitDisabled = computed(() =>
  !inputValue.value.trim() ||
  (composerMode.value === 'plan'
    ? !canCreatePlan.value || store.isAgentBusy
    : isInputDisabled.value)
)
const visiblePluginTools = computed(() => store.availablePluginTools.slice(0, 12))
const hasInlineAgentRun = computed(() =>
  Boolean(
    latestTask.value ||
    taskSteps.value.length ||
    pendingAgentApprovals.value.length ||
    taskArtifacts.value.length ||
    store.currentWorkspace ||
    store.agentErrorMessage
  )
)
const timelineEventItems = computed(() =>
  store.timelineEvents
    .filter((event) => event.eventType !== 'Message')
    .slice(-12)
    .map((event) => {
      const status = resolveTimelineStatus(event)
      const sources = event.agentStepSources ?? []
      return {
        key: `${event.sequence}:${event.eventType}`,
        time: formatTimelineTime(event.createdAt),
        title: timelineEventTitle(event),
        subtitle: timelineEventSubtitle(event),
        status,
        tone: timelineTone(status, event.eventType),
        outputKind: event.agentStepOutputKind,
        resultCount: event.agentStepResultCount ?? sources.length,
        lowConfidence: event.agentStepLowConfidence,
        sources
      }
    })
)
const timelineEventCount = computed(() =>
  store.timelineEvents.filter((event) => event.eventType !== 'Message').length
)
const latestTimelineSummary = computed(() => {
  const items = timelineEventItems.value
  return items.length ? items[items.length - 1]!.title : '暂无执行事件'
})
const inlineRunSubtitle = computed(() => {
  if (store.currentWorkspace) {
    return `${workspaceStatus.value.label} · ${taskArtifacts.value.length} 个产物`
  }

  if (latestTask.value?.workspaceCode) {
    return '执行上下文已建立'
  }

  return '目标和计划将随对话推进'
})
const artifactHeaderMeta = computed(() =>
  store.currentWorkspace
    ? `${taskArtifacts.value.length} 个产物 · ${workspaceFileCount.value} 个文件`
    : '等待产物生成'
)

const suggestions = [
  '查看 DEV-001 最近 24 小时设备日志，并给出根因线索',
  '列出 LINE-A 当前设备状态，生成关键指标和记录摘要',
  '查询 DEV-001 配方版本历史，只做只读分析',
  '根据最近产能数据说明异常波动，不执行任何控制动作'
]

function parseAgentPlan(planJson?: string | null): AgentPlanPreview | null {
  if (!planJson) return null
  try {
    const parsed = JSON.parse(planJson) as AgentPlanPreview
    return parsed && typeof parsed === 'object' ? parsed : null
  } catch {
    return null
  }
}

function statusTone(type?: string) {
  if (type === 'success') return 'success'
  if (type === 'warning') return 'warning'
  if (type === 'danger' || type === 'error') return 'danger'
  return 'neutral'
}

function formatTimelineTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return '--:--'
  }

  return date.toLocaleTimeString('zh-CN', {
    hour: '2-digit',
    minute: '2-digit'
  })
}

function approvalTypeLabel(type?: string | null) {
  if (type === 'Plan') return '计划'
  if (type === 'Tool') return '工具'
  if (type === 'ToolCall') return '工具'
  if (type === 'FinalOutput') return '最终输出'
  if (type === 'Artifact') return '产物'
  return '审批'
}

function approvalDisplayTitle(approval: AgentApprovalRequest) {
  if (approval.type === 'Plan') return '确认执行计划'
  if (approval.type === 'FinalOutput') return '确认最终输出'
  if (approval.type === 'Artifact') return '确认产物'
  if (approval.type === 'Tool' || approval.type === 'ToolCall') return '需要确认后继续'
  return '人工审批请求'
}

function approvalRiskLabel(riskLevel?: string | null) {
  const normalized = riskLevel?.toLowerCase()
  if (normalized === 'critical') return '关键风险'
  if (normalized === 'high') return '高风险'
  if (normalized === 'medium') return '中风险'
  if (normalized === 'low') return '低风险'
  return riskLevel || '待评估'
}

function approvalMetaLine(approval: AgentApprovalRequest) {
  return `${approvalRiskLabel(approval.riskLevel)} · ${approval.reason || '等待人工复核'}`
}

function timelineEventTitle(event: typeof store.timelineEvents[number]) {
  switch (event.eventType) {
    case 'AgentTaskPlanCreated':
      return '计划已生成'
    case 'ApprovalRequested':
      return `${approvalTypeLabel(event.approvalType)}待审批`
    case 'ApprovalDecided':
      return `${approvalTypeLabel(event.approvalType)}已处理`
    case 'AgentTaskStepStarted':
      return '步骤开始'
    case 'AgentTaskStepCompleted':
      return '步骤完成'
    case 'ArtifactReady':
      return '产物就绪'
    case 'FinalOutputReady':
      return '最终输出'
    default:
      return event.eventType
  }
}

function timelineEventSubtitle(event: typeof store.timelineEvents[number]) {
  return event.agentStepTitle ||
    event.approvalTargetName ||
    event.artifactName ||
    event.agentTaskTitle ||
    event.workspaceCode ||
    event.agentTaskGoal ||
    '执行事件'
}

function resolveTimelineStatus(event: typeof store.timelineEvents[number]) {
  return event.approvalStatus ||
    event.agentStepStatus ||
    event.artifactStatus ||
    event.workspaceStatus ||
    event.agentTaskStatus ||
    'Recorded'
}

function formatTimelineScore(score?: number | null) {
  if (typeof score !== 'number' || Number.isNaN(score)) {
    return '相关度 -'
  }

  return `相关度 ${Math.round(score * 100)}%`
}

function timelineTone(status: string, eventType: string): TagTone {
  if (['Approved', 'Completed', 'Final', 'Finalized'].includes(status) || eventType === 'FinalOutputReady') {
    return 'success'
  }

  if (['Rejected', 'Failed', 'Cancelled', 'Expired'].includes(status)) {
    return 'danger'
  }

  if (['Pending', 'Running', 'WaitingPlanApproval', 'WaitingToolApproval', 'WaitingFinalApproval', 'Recorded'].includes(status) ||
      eventType === 'ApprovalRequested' ||
      eventType === 'AgentTaskStepStarted') {
    return 'warning'
  }

  return 'neutral'
}

function sourceModeLabel(value?: string | null) {
  if (!value) {
    return '只读分析'
  }

  if (value === 'SimulationBusiness' || value === 'Simulation') {
    return 'AI 独立模拟业务库'
  }

  if (value.includes('CloudReadonly') || value.includes('CloudReadOnly')) {
    return 'Cloud 只读'
  }

  if (value === 'FreeGoal') {
    return '自由目标'
  }

  if (value === 'workspace' || value === 'Workspace') {
    return '工作区'
  }

  if (value === 'UnknownSource') {
    return '未知来源'
  }

  return value
}

async function sendDirectMessage() {
  const content = inputValue.value.trim()
  if (!content || isInputDisabled.value) return
  inputValue.value = ''
  await store.sendMessage(content)
}

function handleComposerKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault()
    void submitComposer()
  }
}

async function useSuggestion(text: string) {
  inputValue.value = text
  await createAgentPlan()
}

function openFilePicker() {
  fileInput.value?.click()
}

function handleSkillChange(event: Event) {
  const target = event.target as HTMLSelectElement
  planTypeValue.value = target.value || 'auto'
}

function handleKnowledgeBaseChange(event: Event) {
  const target = event.target as HTMLSelectElement
  store.selectKnowledgeBase(target.value || null)
}

async function handleFileChange(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (!file) return
  await store.uploadSessionFile(file)
  target.value = ''
}

async function createAgentPlan() {
  const goal = inputValue.value.trim() || agentGoal.value.trim()
  if (!goal || !canCreatePlan.value) return
  agentGoal.value = goal
  inputValue.value = ''
  await store.planAgentTask(goal)
}

async function submitComposer() {
  if (composerMode.value === 'chat') {
    await sendDirectMessage()
    return
  }

  await createAgentPlan()
}

function setComposerMode(mode: ComposerMode) {
  composerMode.value = mode
}

function togglePluginTool(toolCode: string) {
  store.togglePluginTool(toolCode)
}

function pluginToolLabel(tool: AgentPlannerToolSummary) {
  return tool.displayName || tool.toolCode
}

function pluginToolMeta(tool: AgentPlannerToolSummary) {
  const parts = [
    tool.category || tool.providerKind || '能力',
    tool.requiresApproval ? '需确认' : '只读'
  ]

  return parts.filter(Boolean).join(' · ')
}

async function runLatestTask() {
  if (!latestTask.value || !canRunTask.value) return
  await store.runAgentTask(latestTask.value.id)
}

async function continueLatestTask() {
  if (!latestTask.value || !canContinueTask.value) return
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

async function approveAgentApproval(approvalId: string) {
  const approval = pendingAgentApprovals.value.find((item) => item.id === approvalId)
  if (!approval) return
  const decided = await store.decideAgentApproval(approval, 'approve', 'Approved from inline plan card')
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

async function loadOlderMessages() {
  if (!store.currentSessionId || !scrollContainer.value) {
    return
  }

  const container = scrollContainer.value
  const previousTop = container.scrollTop
  const previousHeight = container.scrollHeight
  preserveScrollAnchor.value = true
  try {
    const changed = await store.loadOlderHistory(store.currentSessionId)
    await nextTick()
    if (changed && scrollContainer.value) {
      scrollContainer.value.scrollTop = previousTop + (scrollContainer.value.scrollHeight - previousHeight)
    }
  } finally {
    preserveScrollAnchor.value = false
  }
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
  }
}

watch(
  [
    () => store.currentMessages,
    () => latestTask.value?.id,
    () => taskSteps.value.map((step) => step.status).join(','),
    () => pendingAgentApprovals.value.length,
    () => taskArtifacts.value.length
  ],
  () => {
    if (!preserveScrollAnchor.value) {
      void scrollToBottom()
    }
  },
  { deep: true }
)

watch(() => store.currentSessionId, () => {
  sessionDrawerVisible.value = false
  void scrollToBottom()
})

onMounted(() => window.addEventListener('resize', handleResize))
onBeforeUnmount(() => window.removeEventListener('resize', handleResize))
</script>

<template>
  <div class="command-workbench">
    <aside v-if="!isMobile" class="session-rail" :class="{ collapsed: uiLayoutStore.isSessionRailCollapsed }">
      <div class="rail-head">
        <div>
          <span>会话列表</span>
          <strong>历史会话</strong>
        </div>
        <button type="button" aria-label="折叠会话栏" @click="uiLayoutStore.toggleSessionRail()">
          <PanelLeftOpen :size="18" />
        </button>
      </div>
      <SessionList class="sessions" />
    </aside>

    <section class="ai-canvas">
      <header class="canvas-header">
        <div class="title-zone">
          <button v-if="isMobile" class="icon-button" type="button" aria-label="打开会话" @click="sessionDrawerVisible = true">
            <PanelLeftOpen :size="20" />
          </button>
          <div>
            <p class="canvas-kicker">对话工作区</p>
            <h1>{{ currentTitle }}</h1>
          </div>
        </div>
        <div class="canvas-toolbar">
          <AiTag :tone="store.isStreaming ? 'warning' : 'success'">
            {{ store.isStreaming ? '生成中' : '就绪' }}
          </AiTag>
          <AiTag :tone="latestPlanIsCloudReadonly ? 'warning' : 'success'">
            {{ latestPlanIsCloudReadonly ? 'Cloud 只读' : '只读分析' }}
          </AiTag>
          <button
            class="soft-action"
            type="button"
            :disabled="!store.currentSessionId"
            @click="store.currentSessionId && store.selectSession(store.currentSessionId, true)"
          >
            <RefreshCw :size="17" />
            刷新
          </button>
        </div>
      </header>

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

        <div v-if="store.hasMoreHistoryBefore && store.currentMessages.length" class="history-loader">
          <button type="button" :disabled="store.isLoadingOlderHistory" @click="loadOlderMessages">
            <RefreshCw :size="16" />
            {{ store.isLoadingOlderHistory ? '加载中' : '加载更早消息' }}
          </button>
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
          <MessageItem
            v-for="message in store.currentMessages"
            :key="message.messageId ?? message.timestamp"
            :message="message"
          />
        </div>

        <section v-if="hasInlineAgentRun" class="inline-agent-run" data-testid="inline-agent-run">
          <header class="run-head">
            <div>
              <span>目标与计划</span>
              <h2>{{ latestTask?.title || agentGoal || '当前任务计划' }}</h2>
              <p>{{ inlineRunSubtitle }}</p>
            </div>
            <AiTag :tone="statusTone(taskStatus.type)">{{ taskStatus.label }}</AiTag>
          </header>

          <div class="run-actions">
            <button type="button" :disabled="!canRunTask || store.isAgentBusy" @click="runLatestTask">
              <Play :size="17" />
              确认执行
            </button>
            <button type="button" :disabled="!canContinueTask || store.isAgentBusy" @click="continueLatestTask">
              <RefreshCw :size="17" />
              继续
            </button>
            <button type="button" :disabled="!canFinalizeWorkspace || store.isAgentBusy" @click="finalizeCurrentWorkspace">
              <FolderOpen :size="17" />
              正式输出
            </button>
          </div>

          <div v-if="store.agentErrorMessage" class="canvas-error inline-error" role="alert">
            <TriangleAlert :size="18" />
            {{ store.agentErrorMessage }}
          </div>

          <section v-if="latestTask" class="run-card plan-card" data-testid="inline-plan-card">
            <div class="section-title">
              <strong>计划</strong>
              <AiTag :tone="statusTone(taskStatus.type)">
                {{ taskStatus.label }}
              </AiTag>
            </div>
            <div class="plan-summary">
              <strong>{{ latestTask.title }}</strong>
              <span>{{ latestTask.goal }}</span>
            </div>
            <div v-if="taskSteps.length" class="plan-steps-preview" data-testid="plan-steps-preview">
              <div v-for="step in taskSteps.slice(0, 6)" :key="step.id" class="plan-step-preview">
                <i>{{ step.stepIndex }}</i>
                <div>
                  <strong>{{ step.title }}</strong>
                  <span>{{ step.description || '按计划执行该步骤' }}</span>
                </div>
                <AiTag :tone="step.requiresApproval ? 'warning' : 'neutral'">
                  {{ step.requiresApproval ? '需确认' : '只读' }}
                </AiTag>
              </div>
            </div>
            <div v-if="blockedStep" class="blocked-step">
              <span>当前阻塞</span>
              <strong>{{ blockedStep.title }}</strong>
            </div>
            <details class="plan-detail-fold" data-testid="plan-technical-details">
              <summary>
                <SlidersHorizontal :size="15" />
                <span>技术详情</span>
                <AiTag tone="neutral">详情</AiTag>
              </summary>
              <div class="plan-grid">
                <div>
                  <span>来源</span>
                  <strong>{{ latestPlanSource }}</strong>
                </div>
                <div>
                  <span>工具目录</span>
                  <strong>v{{ latestPlanToolCatalogVersion ?? '-' }} / {{ latestPlanVisibleToolCount }}</strong>
                </div>
                <div>
                  <span>审批点</span>
                  <strong>{{ latestPlan?.approvalCheckpoints?.length ?? 0 }}</strong>
                </div>
                <div>
                  <span>风险</span>
                  <strong>{{ latestPlanRiskLine }}</strong>
                </div>
              </div>
              <div v-if="latestPlan?.forcedStepCodes?.length" class="chip-row">
                <span v-for="code in latestPlan.forcedStepCodes" :key="code">{{ code }}</span>
              </div>
              <div v-if="latestPlan?.plannerFallbackReason" class="planner-warning">
                <TriangleAlert :size="15" />
                <span>{{ latestPlan.plannerFallbackReason }}</span>
              </div>
            </details>
          </section>

          <details v-if="latestTask || taskSteps.length || timelineEventItems.length" class="run-card runtime-run-card" data-testid="inline-runtime-card">
            <summary class="runtime-summary" data-testid="inline-runtime-summary">
              <span class="timeline-summary-main">
                <ListChecks :size="18" />
                <span>
                  <strong>运行详情</strong>
                  <small>执行记录与安全边界</small>
                </span>
              </span>
              <AiTag tone="neutral">详情</AiTag>
            </summary>

            <div class="runtime-sections">
              <section class="runtime-section-block">
                <div class="runtime-section-title">
                  <strong>状态</strong>
                  <span>阶段、审批和工作区</span>
                </div>
                <div class="status-grid">
                  <div v-for="item in agentStageCards" :key="item.label" class="status-tile">
                    <span>{{ item.label }}</span>
                    <AiTag :tone="statusTone(item.type)">{{ item.value }}</AiTag>
                  </div>
                  <div class="status-tile">
                    <span>草稿 / 正式</span>
                    <strong class="ai-number">{{ draftArtifactCount }}/{{ finalArtifactCount }}</strong>
                  </div>
                </div>
              </section>

              <section class="runtime-section-block">
                <div class="runtime-section-title">
                  <strong>安全边界</strong>
                  <span>AICopilot 只读边界和现场声明</span>
                </div>
                <div class="boundary-runtime" data-testid="inline-boundary-row">
                  <ShieldCheck :size="20" />
                  <div>
                    <strong>Cloud 只读边界</strong>
                    <span>现场确认：{{ onsiteStatus.label }}</span>
                  </div>
                  <button type="button" @click="store.confirmOnsitePresence(30)">确认在岗</button>
                </div>
              </section>

              <section v-if="timelineEventItems.length" class="runtime-section-block">
                <div class="runtime-section-title">
                  <strong>事件</strong>
                  <span>最新：{{ latestTimelineSummary }}</span>
                </div>
                <div class="timeline-list">
                  <div v-for="item in timelineEventItems" :key="item.key" class="timeline-row">
                    <time>{{ item.time }}</time>
                    <div class="timeline-row-main">
                      <strong>{{ item.title }}</strong>
                      <span>{{ item.subtitle }}</span>
                      <details v-if="item.outputKind === 'RagSearch' && item.sources.length" class="timeline-result-fold">
                        <summary>
                          检索结果 · {{ item.resultCount }} 条
                          <template v-if="item.lowConfidence"> · 低置信度</template>
                        </summary>
                        <div class="timeline-source-list">
                          <article v-for="source in item.sources" :key="`${item.key}:${source.documentId}:${source.chunkIndex}`" class="timeline-source-item">
                            <strong>{{ source.documentName || `文档 ${source.documentId || '-'}` }}</strong>
                            <small>
                              {{ formatTimelineScore(source.score) }}
                              <template v-if="source.isLowConfidence"> · 低置信度</template>
                              <template v-if="source.lowConfidenceReason"> · {{ source.lowConfidenceReason }}</template>
                            </small>
                            <em v-if="source.textPreview">{{ source.textPreview }}</em>
                          </article>
                        </div>
                      </details>
                    </div>
                    <AiTag :tone="item.tone">{{ item.status }}</AiTag>
                  </div>
                </div>
              </section>

              <section class="runtime-section-block">
                <div class="runtime-section-title">
                  <strong>步骤</strong>
                  <span>{{ completedStepCount }}/{{ taskSteps.length }} 已完成</span>
                </div>
                <div v-if="taskSteps.length" class="step-list">
                  <div v-for="step in taskSteps" :key="step.id" class="step-row">
                    <i>{{ step.stepIndex }}</i>
                    <div>
                      <strong>{{ step.title }}</strong>
                      <span v-if="step.errorMessage" class="danger-text">{{ step.errorMessage }}</span>
                      <details v-if="step.toolCode || step.description" class="step-detail-fold">
                        <summary>详情</summary>
                        <span v-if="step.description">{{ step.description }}</span>
                        <code v-if="step.toolCode">{{ step.toolCode }}</code>
                      </details>
                    </div>
                    <AiTag :tone="step.status === 'Completed' ? 'success' : step.status === 'Failed' ? 'danger' : step.requiresApproval ? 'warning' : 'neutral'">
                      {{ step.status }}
                    </AiTag>
                  </div>
                </div>
                <div v-else class="panel-empty">暂无步骤</div>
              </section>
            </div>
          </details>

          <section v-if="approvalGroups.length" class="run-card approvals-run-card" data-testid="inline-approval-card">
            <div class="section-title">
              <strong>审批</strong>
              <span>{{ pendingAgentApprovals.length }} 项待处理</span>
            </div>
            <div v-for="group in approvalGroups" :key="group.label" class="approval-group">
              <div class="group-title">{{ group.label }}</div>
              <div v-for="approval in group.approvals" :key="approval.id" class="approval-row">
                <div>
                  <strong>{{ approvalDisplayTitle(approval) }}</strong>
                  <span>{{ approvalMetaLine(approval) }}</span>
                  <details class="approval-detail-fold" data-testid="approval-detail-fold">
                    <summary>审批详情</summary>
                    <dl class="approval-detail-grid">
                      <div>
                        <dt>类型</dt>
                        <dd>{{ approvalTypeLabel(approval.type) }}</dd>
                      </div>
                      <div>
                        <dt>对象</dt>
                        <dd class="mono">{{ approval.targetName }}</dd>
                      </div>
                      <div>
                        <dt>目标 ID</dt>
                        <dd class="mono">{{ approval.targetId }}</dd>
                      </div>
                      <div v-if="approval.workspaceCode">
                        <dt>工作区</dt>
                        <dd class="mono">{{ approval.workspaceCode }}</dd>
                      </div>
                    </dl>
                  </details>
                </div>
                <div class="approval-actions">
                  <button type="button" aria-label="批准审批" :disabled="store.isAgentBusy" @click="approveAgentApproval(approval.id)">
                    <Check :size="16" />
                  </button>
                  <button type="button" aria-label="驳回审批" :disabled="store.isAgentBusy" @click="rejectAgentApproval(approval.id)">
                    <X :size="16" />
                  </button>
                </div>
              </div>
            </div>
          </section>

          <section v-if="store.currentWorkspace || taskArtifacts.length" class="run-card" data-testid="inline-artifact-card">
            <div class="section-title">
              <strong>{{ workspaceStatus.label }}</strong>
              <span>{{ artifactHeaderMeta }}</span>
            </div>
            <div v-if="chartBars.length" class="chart-preview">
              <div class="chart-preview-head">
                <span>图表预览</span>
                <small>{{ sourceModeLabel(store.chartPreview?.sourceLabel || store.chartPreview?.sourceMode || store.chartPreview?.source || 'workspace') }}</small>
              </div>
              <div v-for="bar in chartBars" :key="bar.label" class="chart-bar-row">
                <span>{{ bar.label }}</span>
                <div><i :style="{ width: bar.width }" /></div>
                <strong>{{ bar.value }}</strong>
              </div>
            </div>

            <details class="artifact-detail-fold" data-testid="artifact-detail-fold">
              <summary class="artifact-detail-summary">
                <span class="timeline-summary-main">
                  <FolderOpen :size="18" />
                  <span>
                    <strong>产物详情</strong>
                    <small>{{ taskArtifacts.length }} 个产物 · {{ workspaceFileCount }} 个工作区文件</small>
                  </span>
                </span>
                <AiTag tone="neutral">详情</AiTag>
              </summary>
              <div class="artifact-summary">
                <div>
                  <span>工作区</span>
                  <strong>{{ store.currentWorkspace?.workspaceCode || '-' }}</strong>
                </div>
                <div>
                  <span>文件</span>
                  <strong>{{ workspaceFileCount }}</strong>
                </div>
                <div>
                  <span>草稿</span>
                  <strong>{{ draftArtifacts.length }}</strong>
                </div>
                <div>
                  <span>正式</span>
                  <strong>{{ finalArtifacts.length }}</strong>
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
                        {{ artifact.sourceLabel || sourceModeLabel(artifact.sourceMode || 'UnknownSource') }}
                        <template v-if="artifact.boundary"> · {{ artifact.boundary }}</template>
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
                <pre v-if="currentArtifactPreview.content" class="artifact-preview-content">{{ currentArtifactPreview.content.slice(0, 1600) }}</pre>
                <div v-else-if="currentArtifactPreview.rows?.length" class="artifact-preview-table">
                  <div class="artifact-preview-table-head">
                    <span v-for="column in currentArtifactPreview.columns" :key="column">{{ column }}</span>
                  </div>
                  <div v-for="(row, index) in currentArtifactPreview.rows.slice(0, 5)" :key="index" class="artifact-preview-table-row">
                    <span v-for="column in currentArtifactPreview.columns" :key="column">{{ row[column] ?? '-' }}</span>
                  </div>
                </div>
              </div>
            </details>
            <button class="inline-secondary-action" type="button" :disabled="!canSubmitFinalReview || store.isAgentBusy" @click="submitFinalReview">
              提交最终审批
            </button>
          </section>
        </section>
      </div>

      <footer class="command-composer">
        <div class="composer-mode-bar">
          <div class="mode-switch" role="group" aria-label="输入模式">
            <button
              type="button"
              :class="{ active: composerMode === 'plan' }"
              @click="setComposerMode('plan')"
            >
              <ListChecks :size="16" />
              计划模式
            </button>
            <button
              type="button"
              :class="{ active: composerMode === 'chat' }"
              @click="setComposerMode('chat')"
            >
              <MessageCircle :size="16" />
              聊天模式
            </button>
          </div>
          <button
            class="composer-add-button"
            type="button"
            :aria-expanded="composerOptionsOpen"
            @click="composerOptionsOpen = !composerOptionsOpen"
          >
            <Plus :size="17" />
            添加
          </button>
          <span class="composer-context-line">
            {{ selectedPlanTypeLabel }} · {{ selectedPluginLine }}
          </span>
        </div>

        <div v-if="composerOptionsOpen" class="composer-options-panel">
          <input ref="fileInput" class="hidden-file" type="file" @change="handleFileChange">
          <section class="composer-option-group">
            <div class="option-title">
              <Sparkles :size="17" />
              <span>计划类型</span>
            </div>
            <label class="select-field">
              <select
                :value="planTypeValue"
                :disabled="store.isAgentBusy"
                aria-label="选择计划类型"
                @change="handleSkillChange"
              >
                <option value="auto">自动识别</option>
                <option
                  v-for="skill in store.availableSkills"
                  :key="skill.skillCode"
                  :value="skill.skillCode"
                >
                  {{ skill.displayName }}
                </option>
              </select>
            </label>
            <p>{{ store.selectedSkill?.description || '系统会根据目标自动选择最合适的只读分析路径。' }}</p>
          </section>

          <section class="composer-option-group">
            <div class="option-title">
              <FileUp :size="17" />
              <span>输入材料</span>
            </div>
            <button class="tool-button" type="button" :disabled="store.isAgentBusy" @click="openFilePicker">
              上传文件
            </button>
            <span class="uploaded-hint">
              {{ store.uploadedFiles.length ? `${store.uploadedFiles.length} 个输入文件` : '未上传文件' }}
            </span>
          </section>

          <section
            v-if="store.selectedSkillSupportsKnowledge && store.availableKnowledgeBases.length"
            class="composer-option-group"
          >
            <div class="option-title">
              <FolderOpen :size="17" />
              <span>知识库</span>
            </div>
            <label class="select-field">
              <select
                :value="store.selectedKnowledgeBaseId || ''"
                :disabled="store.isAgentBusy"
                aria-label="选择知识库"
                @change="handleKnowledgeBaseChange"
              >
                <option value="">不使用知识库</option>
                <option
                  v-for="knowledgeBase in store.availableKnowledgeBases"
                  :key="knowledgeBase.id"
                  :value="knowledgeBase.id"
                >
                  {{ knowledgeBase.name }}
                </option>
              </select>
            </label>
            <p>{{ store.selectedKnowledgeBase?.description || '管理员建库后，普通用户可选择资料参与分析。' }}</p>
          </section>

          <section class="composer-option-group plugin-option-group">
            <div class="option-title">
              <Wrench :size="17" />
              <span>插件能力</span>
              <small v-if="store.isLoadingPluginTools">加载中</small>
            </div>
            <div v-if="visiblePluginTools.length" class="plugin-tool-grid">
              <button
                v-for="tool in visiblePluginTools"
                :key="tool.toolCode"
                type="button"
                class="plugin-tool-chip"
                :class="{ active: store.selectedToolCodes.includes(tool.toolCode) }"
                :title="tool.description"
                @click="togglePluginTool(tool.toolCode)"
              >
                <strong>{{ pluginToolLabel(tool) }}</strong>
                <span>{{ pluginToolMeta(tool) }}</span>
              </button>
            </div>
            <div v-else class="panel-empty compact">当前计划类型暂无可选插件能力</div>
            <button
              v-if="store.selectedToolCodes.length"
              class="quiet-link"
              type="button"
              @click="store.clearPluginTools()"
            >
              清空插件选择
            </button>
          </section>
        </div>

        <div class="composer-input-row">
          <textarea
            v-model="inputValue"
            :disabled="isInputDisabled"
            :placeholder="composerPlaceholder"
            rows="1"
            @keydown="handleComposerKeydown"
          />
          <button
            class="send-button"
            type="button"
            :disabled="isComposerSubmitDisabled"
            :aria-label="composerPrimaryLabel"
            @click="submitComposer"
          >
            <component :is="composerPrimaryIcon" :size="19" />
            <span>{{ composerPrimaryLabel }}</span>
          </button>
        </div>
      </footer>
    </section>

    <div v-if="sessionDrawerVisible" class="mobile-overlay" @click.self="sessionDrawerVisible = false">
      <aside class="mobile-drawer left">
        <button class="drawer-close" type="button" aria-label="关闭会话" @click="sessionDrawerVisible = false">
          <X :size="18" />
        </button>
        <SessionList />
      </aside>
    </div>
  </div>
</template>

<style scoped>
.command-workbench {
  display: grid;
  grid-template-columns: minmax(260px, 300px) minmax(0, 1fr);
  height: 100%;
  min-height: 0;
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.74);
  border-radius: 28px;
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-card);
}

.session-rail {
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  min-width: 0;
  min-height: 0;
  overflow: hidden;
  border-right: 1px solid var(--ai-border);
  background: color-mix(in srgb, var(--ai-surface) 90%, var(--ai-surface-soft));
}

.rail-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 18px;
}

.rail-head div,
.section-title,
.step-row div,
.approval-row div,
.artifact-row div {
  display: grid;
  gap: 4px;
  min-width: 0;
}

.rail-head span,
.canvas-kicker,
.run-head span,
.section-title span,
.status-tile span,
.step-row span,
.approval-row span,
.artifact-row span,
.artifact-source-line,
.panel-empty,
.uploaded-hint {
  color: var(--ai-text-muted);
  font-size: 12px;
}

.rail-head strong {
  font-size: 18px;
  font-weight: 900;
}

.rail-head button,
.icon-button,
.approval-actions button,
.artifact-actions button,
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

.ai-canvas {
  display: grid;
  grid-template-rows: auto minmax(0, 1fr) auto;
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
.run-actions,
.run-actions button,
.inline-secondary-action,
.boundary-runtime,
.boundary-runtime button {
  display: flex;
  align-items: center;
}

.title-zone {
  gap: 12px;
  min-width: 0;
}

.canvas-kicker {
  margin: 0 0 4px;
  font-weight: 850;
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

.soft-action,
.tool-button,
.run-actions button,
.inline-secondary-action,
.boundary-runtime button {
  gap: 8px;
  min-height: 38px;
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
.run-actions button:disabled,
.inline-secondary-action:disabled,
.send-button:disabled {
  cursor: not-allowed;
  opacity: 0.48;
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
  font-weight: 800;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.message-viewport {
  min-height: 0;
  overflow-y: auto;
  padding: 22px;
  background: var(--ai-canvas);
}

.message-list,
.inline-agent-run,
.history-loader,
.loading-lines {
  display: grid;
  gap: 16px;
  max-width: 980px;
  margin: 0 auto;
}

.history-loader {
  justify-items: center;
  margin-bottom: 14px;
}

.history-loader button {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  min-height: 36px;
  border: 1px solid var(--ai-border);
  border-radius: 8px;
  padding: 0 12px;
  background: var(--ai-surface);
  color: var(--ai-text-muted);
  font-weight: 800;
}

.history-loader button:disabled {
  cursor: progress;
  opacity: 0.62;
}

.inline-agent-run {
  margin-top: 18px;
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

.inline-error {
  margin: 0;
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
  border-radius: 24px;
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

.run-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 14px;
  border: 1px solid rgba(14, 165, 166, 0.2);
  border-radius: 20px;
  padding: 16px;
  background: rgba(240, 253, 250, 0.74);
}

.run-head h2 {
  margin: 4px 0 0;
  color: var(--ai-text);
  font-size: 18px;
  font-weight: 950;
  line-height: 1.3;
}

.run-head p {
  margin: 5px 0 0;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.run-actions {
  gap: 8px;
  flex-wrap: wrap;
}

.run-actions button:first-child {
  background: var(--ai-graphite);
  color: white;
}

.run-card {
  display: grid;
  gap: 12px;
  border: 1px solid var(--ai-border);
  border-radius: 18px;
  padding: 14px;
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-xs);
}

.runtime-run-card {
  gap: 0;
  padding: 0;
  overflow: hidden;
}

.runtime-summary,
.artifact-detail-summary {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 12px;
  align-items: center;
  padding: 14px;
  cursor: pointer;
  list-style: none;
}

.runtime-summary::-webkit-details-marker,
.artifact-detail-summary::-webkit-details-marker {
  display: none;
}

.timeline-summary-main {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
}

.timeline-summary-main > span {
  display: grid;
  gap: 3px;
  min-width: 0;
}

.timeline-summary-main strong,
.timeline-summary-main small {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.timeline-summary-main small,
.timeline-row span,
.timeline-row time {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.timeline-list {
  display: grid;
  gap: 8px;
}

.status-grid {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 10px;
}

.runtime-sections {
  display: grid;
  gap: 14px;
  padding: 0 14px 14px;
}

.runtime-section-block {
  display: grid;
  gap: 10px;
  min-width: 0;
}

.runtime-section-title {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  min-width: 0;
}

.runtime-section-title strong {
  color: var(--ai-text);
  font-size: 13px;
  font-weight: 900;
}

.runtime-section-title span {
  min-width: 0;
  overflow: hidden;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.timeline-row {
  display: grid;
  grid-template-columns: 58px minmax(0, 1fr) auto;
  gap: 10px;
  align-items: center;
  border: 1px solid var(--ai-border);
  border-radius: 15px;
  padding: 10px;
  background: var(--ai-surface-soft);
}

.timeline-row div {
  display: grid;
  gap: 3px;
  min-width: 0;
}

.timeline-row-main {
  align-content: center;
}

.timeline-row strong,
.timeline-row span {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.timeline-result-fold {
  min-width: 0;
}

.timeline-result-fold summary {
  width: fit-content;
  cursor: pointer;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 900;
}

.timeline-source-list {
  display: grid;
  gap: 6px;
  margin-top: 8px;
}

.timeline-source-item {
  display: grid;
  gap: 3px;
  min-width: 0;
  border: 1px solid var(--ai-border);
  border-radius: 10px;
  padding: 8px;
  background: var(--ai-surface);
}

.timeline-source-item strong,
.timeline-source-item small,
.timeline-source-item em {
  min-width: 0;
  overflow-wrap: anywhere;
  white-space: normal;
}

.timeline-source-item strong {
  color: var(--ai-text);
  font-size: 12px;
  font-style: normal;
  font-weight: 900;
}

.timeline-source-item small,
.timeline-source-item em {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-style: normal;
  font-weight: 800;
  line-height: 1.55;
}

.section-title {
  grid-auto-flow: column;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.section-title strong {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.plan-grid,
.artifact-summary {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 8px;
}

.plan-grid div,
.artifact-summary div,
.plan-summary,
.blocked-step,
.plan-step-preview {
  display: grid;
  gap: 4px;
  min-width: 0;
  border: 1px solid var(--ai-border);
  border-radius: 14px;
  padding: 10px;
  background: var(--ai-surface-soft);
}

.plan-grid span,
.artifact-summary span,
.plan-summary span,
.blocked-step span,
.plan-step-preview span {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.plan-grid strong,
.artifact-summary strong,
.plan-summary strong,
.blocked-step strong,
.plan-step-preview strong {
  min-width: 0;
  overflow: hidden;
  color: var(--ai-text);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.plan-steps-preview {
  display: grid;
  gap: 8px;
}

.plan-step-preview {
  grid-template-columns: 30px minmax(0, 1fr) auto;
  align-items: center;
}

.plan-step-preview i {
  display: grid;
  width: 24px;
  height: 24px;
  place-items: center;
  border-radius: 999px;
  background: var(--ai-graphite);
  color: white;
  font-style: normal;
  font-size: 12px;
  font-weight: 900;
}

.plan-step-preview div {
  display: grid;
  gap: 3px;
  min-width: 0;
}

.plan-detail-fold,
.step-detail-fold,
.approval-detail-fold {
  min-width: 0;
}

.plan-detail-fold {
  overflow: hidden;
  border: 1px solid var(--ai-border);
  border-radius: 14px;
  background: var(--ai-surface-soft);
}

.plan-detail-fold summary,
.step-detail-fold summary,
.approval-detail-fold summary {
  display: inline-flex;
  min-height: 32px;
  cursor: pointer;
  align-items: center;
  gap: 7px;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 900;
}

.plan-detail-fold summary {
  display: flex;
  justify-content: space-between;
  padding: 8px 10px;
}

.plan-detail-fold summary > span {
  min-width: 0;
  flex: 1;
}

.plan-detail-fold .plan-grid,
.plan-detail-fold .chip-row,
.plan-detail-fold .planner-warning {
  margin: 0 10px 10px;
}

.step-detail-fold,
.approval-detail-fold {
  margin-top: 3px;
}

.step-detail-fold span,
.step-detail-fold code,
.approval-detail-fold dd {
  display: block;
  min-width: 0;
  margin-top: 3px;
  overflow: hidden;
  color: var(--ai-text-muted);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.step-detail-fold code,
.approval-detail-fold .mono {
  border-radius: 8px;
  padding: 2px 6px;
  background: var(--ai-surface-soft);
  color: var(--ai-graphite);
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
  font-size: 12px;
  font-weight: 800;
}

.approval-detail-grid {
  display: grid;
  gap: 6px;
  margin: 2px 0 0;
}

.approval-detail-grid div {
  display: grid;
  grid-template-columns: 64px minmax(0, 1fr);
  gap: 8px;
  align-items: center;
}

.approval-detail-grid dt {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 850;
}

.approval-detail-grid dd {
  margin: 0;
  color: var(--ai-text);
  font-size: 12px;
  font-weight: 800;
}

.intent-line,
.chip-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
}

.intent-line span,
.chip-row span {
  border: 1px solid var(--ai-border);
  border-radius: 999px;
  padding: 4px 8px;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.planner-warning {
  display: flex;
  align-items: flex-start;
  gap: 7px;
  color: #92400e;
  font-size: 12px;
  line-height: 1.35;
}

.step-list,
.approval-group,
.artifact-group {
  display: grid;
  gap: 8px;
}

.step-row,
.approval-row,
.artifact-row {
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
.artifact-row {
  grid-template-columns: minmax(0, 1fr) auto;
}

.step-row strong,
.step-row span,
.approval-row strong,
.approval-row span,
.artifact-row strong,
.artifact-row span {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.approval-actions,
.artifact-actions {
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
  min-height: 84px;
  place-items: center;
  border: 1px dashed var(--ai-border);
  border-radius: 16px;
}

.chart-preview {
  display: grid;
  gap: 8px;
  border: 1px solid var(--ai-border);
  border-radius: 16px;
  padding: 12px;
  background: var(--ai-surface-soft);
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

.artifact-preview-panel {
  display: grid;
  gap: 10px;
  border: 1px solid var(--ai-border);
  border-radius: 16px;
  padding: 12px;
  background: var(--ai-surface-soft);
}

.artifact-detail-fold {
  display: grid;
  gap: 0;
  overflow: hidden;
  border: 1px solid var(--ai-border);
  border-radius: 16px;
  background: var(--ai-surface-soft);
}

.artifact-detail-fold .artifact-summary,
.artifact-detail-fold .artifact-group,
.artifact-detail-fold .artifact-preview-panel,
.artifact-detail-fold .panel-empty {
  margin: 0 12px 12px;
}

.artifact-detail-fold .artifact-summary {
  margin-top: 0;
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
  background: var(--ai-surface);
  font-weight: 800;
}

.inline-secondary-action {
  justify-content: center;
  width: max-content;
}

.boundary-runtime {
  justify-content: space-between;
  gap: 12px;
  border: 1px solid rgba(63, 111, 115, 0.18);
  border-radius: 16px;
  padding: 12px;
  background: var(--ai-surface-soft);
  color: var(--ai-text);
}

.boundary-runtime div {
  display: grid;
  gap: 3px;
  min-width: 0;
  margin-right: auto;
}

.boundary-runtime span {
  color: var(--ai-text-muted);
  font-size: 12px;
}

.boundary-runtime button {
  background: var(--ai-surface);
  color: var(--ai-text);
}

.danger-text {
  color: #b42318;
}

.command-composer {
  display: grid;
  gap: 10px;
  border-top: 1px solid var(--ai-border);
  padding: 14px 18px 18px;
  background: rgba(251, 250, 247, 0.88);
  box-shadow: 0 -12px 30px rgba(70, 64, 55, 0.06);
}

.composer-mode-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.mode-switch {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  border: 1px solid var(--ai-border);
  border-radius: 999px;
  padding: 4px;
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-xs);
}

.mode-switch button,
.composer-add-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  min-height: 34px;
  border: 0;
  border-radius: 999px;
  padding: 0 12px;
  background: transparent;
  color: var(--ai-text-muted);
  cursor: pointer;
  font-weight: 900;
}

.mode-switch button.active {
  background: var(--ai-graphite);
  color: white;
  box-shadow: var(--ai-shadow-xs);
}

.composer-add-button {
  border: 1px solid var(--ai-border);
  background: var(--ai-surface);
}

.composer-context-line {
  min-width: 0;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 850;
}

.hidden-file {
  display: none;
}

.tool-button {
  min-height: 34px;
  color: var(--ai-text-muted);
}

.skill-picker {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  min-height: 34px;
  max-width: 220px;
  border: 1px solid rgba(63, 111, 115, 0.18);
  border-radius: 999px;
  padding: 0 10px;
  background: rgba(255, 255, 255, 0.78);
  color: var(--ai-accent);
  box-shadow: var(--ai-shadow-xs);
}

.skill-picker select {
  min-width: 0;
  max-width: 168px;
  border: 0;
  outline: none;
  background: transparent;
  color: var(--ai-text);
  font: inherit;
  font-size: 13px;
  font-weight: 850;
}

.skill-picker select:disabled {
  color: var(--ai-text-muted);
  cursor: not-allowed;
}

.uploaded-hint {
  color: var(--ai-text-muted);
  font-weight: 800;
}

.composer-options-panel {
  display: grid;
  grid-template-columns: minmax(190px, 0.85fr) minmax(150px, 0.65fr) minmax(200px, 1fr) minmax(280px, 1.4fr);
  gap: 10px;
  border: 1px solid var(--ai-border);
  border-radius: 18px;
  padding: 12px;
  background: rgba(255, 255, 255, 0.72);
  box-shadow: var(--ai-shadow-xs);
}

.composer-option-group {
  display: grid;
  align-content: start;
  gap: 8px;
  min-width: 0;
}

.option-title {
  display: flex;
  align-items: center;
  gap: 7px;
  min-width: 0;
  color: var(--ai-text);
  font-size: 13px;
  font-weight: 950;
}

.option-title span {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.option-title small,
.composer-option-group p {
  margin: 0;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
  line-height: 1.5;
}

.select-field {
  min-width: 0;
  border: 1px solid var(--ai-border);
  border-radius: 12px;
  background: var(--ai-surface);
}

.select-field select {
  width: 100%;
  min-height: 36px;
  border: 0;
  outline: none;
  padding: 0 10px;
  background: transparent;
  color: var(--ai-text);
  font: inherit;
  font-size: 13px;
  font-weight: 850;
}

.plugin-option-group {
  min-width: 0;
}

.plugin-tool-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 7px;
}

.plugin-tool-chip {
  display: grid;
  gap: 3px;
  min-width: 0;
  border: 1px solid var(--ai-border);
  border-radius: 13px;
  padding: 9px 10px;
  background: var(--ai-surface);
  color: var(--ai-text);
  cursor: pointer;
  text-align: left;
}

.plugin-tool-chip.active {
  border-color: rgba(63, 111, 115, 0.35);
  background: rgba(240, 253, 250, 0.86);
  box-shadow: 0 0 0 3px rgba(63, 111, 115, 0.09);
}

.plugin-tool-chip strong,
.plugin-tool-chip span {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.plugin-tool-chip strong {
  font-size: 12px;
  font-weight: 950;
}

.plugin-tool-chip span {
  color: var(--ai-text-muted);
  font-size: 11px;
  font-weight: 800;
}

.quiet-link {
  width: fit-content;
  border: 0;
  background: transparent;
  color: var(--ai-accent);
  cursor: pointer;
  font-size: 12px;
  font-weight: 900;
}

.panel-empty.compact {
  padding: 10px;
  font-size: 12px;
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
  gap: 8px;
  justify-content: center;
  min-width: 112px;
  height: 46px;
  border: 0;
  border-radius: 999px;
  padding: 0 18px;
  background: var(--ai-lime);
  color: var(--ai-graphite);
  cursor: pointer;
  font-weight: 900;
  white-space: nowrap;
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

.drawer-close {
  margin-left: auto;
  margin-bottom: 14px;
}

@media (max-width: 1280px) {
  .command-workbench {
    grid-template-columns: 260px minmax(0, 1fr);
  }

  .status-grid {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }

  .plan-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .composer-options-panel {
    grid-template-columns: repeat(2, minmax(0, 1fr));
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

  .message-viewport {
    padding: 16px;
  }
}

@media (max-width: 720px) {
  .status-grid,
  .plan-grid,
  .artifact-summary {
    grid-template-columns: minmax(0, 1fr);
  }

  .run-head,
  .boundary-runtime {
    align-items: flex-start;
    flex-direction: column;
  }

  .inline-secondary-action {
    width: 100%;
  }

  .step-row,
  .approval-row,
  .artifact-row,
  .timeline-row,
  .plan-step-preview {
    grid-template-columns: minmax(0, 1fr);
  }

  .composer-options-panel,
  .plugin-tool-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .composer-input-row {
    align-items: stretch;
    flex-direction: column;
    padding: 10px;
  }

  .send-button {
    width: 100%;
  }

  .approval-actions,
  .artifact-actions {
    justify-content: flex-start;
  }
}
</style>
