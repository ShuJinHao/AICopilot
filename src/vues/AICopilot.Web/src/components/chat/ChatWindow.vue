<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  ArrowUp,
  Check,
  ChevronRight,
  Download,
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
const fileInput = ref<HTMLInputElement | null>(null)
const scrollContainer = ref<HTMLElement | null>(null)
const isMobile = ref(typeof window !== 'undefined' ? window.innerWidth < 1024 : false)
const sessionDrawerVisible = ref(false)

const {
  latestTask,
  taskHistory,
  taskSteps,
  taskArtifacts,
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
        <AiTag v-else tone="dark">Cloud 只读</AiTag>
      </div>

      <div class="agent-tabs" role="tablist" aria-label="Agent 工作台">
        <button
          v-for="tab in agentTabs"
          :key="tab.value"
          type="button"
          :class="{ active: uiLayoutStore.agentWorkbenchTab === tab.value }"
          @click="setAgentTab(tab.value)"
        >
          {{ tab.label }}
          <span v-if="tab.count !== undefined">{{ tab.count }}</span>
        </button>
      </div>

      <div class="agent-tab-content">
        <section v-if="uiLayoutStore.agentWorkbenchTab === 'plan'" class="tab-stack">
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
          <div v-if="chartBars.length" class="chart-preview">
            <div class="chart-preview-head">
              <span>图表预览</span>
              <small>{{ store.chartPreview?.source || 'workspace' }}</small>
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
                  <span>v{{ artifact.version }} · step {{ artifact.generatedByStepOrder ?? '-' }} · {{ artifact.approvalStatus || artifact.status }}</span>
                </div>
                <button type="button" aria-label="下载产物" @click="downloadArtifact(artifact.id)">
                  <Download :size="16" />
                </button>
              </div>
            </div>
          </template>
          <div v-else class="panel-empty">暂无产物</div>
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
              </div>
              <AiTag :tone="item.result === 'Succeeded' ? 'success' : 'danger'">{{ item.result }}</AiTag>
            </div>
          </div>
          <div v-else class="panel-empty">暂无审计记录</div>
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
