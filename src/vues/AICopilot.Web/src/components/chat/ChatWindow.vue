<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  Connection,
  Document,
  Download,
  Files,
  Fold,
  Operation,
  Promotion,
  Refresh,
  Tickets,
  Upload,
  Warning
} from '@element-plus/icons-vue'
import { useAgentWorkbench } from '@/composables/useAgentWorkbench'
import { useChatStore } from '@/stores/chatStore'
import MessageItem from './MessageItem.vue'
import SessionList from './SessionList.vue'

const store = useChatStore()
const inputValue = ref('')
const agentGoal = ref('')
const fileInput = ref<HTMLInputElement | null>(null)
const scrollContainer = ref<HTMLElement | null>(null)
const isMobile = ref(typeof window !== 'undefined' ? window.innerWidth < 980 : false)
const sessionDrawerVisible = ref(false)
const agentPanelVisible = ref(false)

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
  canApprovePlan,
  canContinueTask,
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

async function sendMessage() {
  const content = inputValue.value.trim()
  if (!content || isInputDisabled.value) return
  inputValue.value = ''
  await store.sendMessage(content)
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

async function approveAndRunLatestTask() {
  if (!latestTask.value || !canApprovePlan.value) return
  await store.approveAndRunAgentTask(latestTask.value.id)
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
  isMobile.value = window.innerWidth < 980
  if (!isMobile.value) {
    sessionDrawerVisible.value = false
    agentPanelVisible.value = false
  }
}

watch(() => store.currentMessages, () => void scrollToBottom(), { deep: true })
watch(() => store.currentSessionId, () => {
  sessionDrawerVisible.value = false
  agentPanelVisible.value = false
  void scrollToBottom()
})

onMounted(() => window.addEventListener('resize', handleResize))
onBeforeUnmount(() => window.removeEventListener('resize', handleResize))
</script>

<template>
  <div class="workspace">
    <aside v-if="!isMobile" class="workbench-rail">
      <SessionList class="sessions" />
      <section class="rail-task-history">
        <header>
          <div>
            <h2>任务历史</h2>
            <span>{{ taskHistory.length }} 个 Agent 任务</span>
          </div>
          <el-icon><Tickets /></el-icon>
        </header>
        <div class="rail-task-list">
          <button
            v-for="task in taskHistory"
            :key="task.id"
            class="rail-task-item"
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

    <section class="chat-main">
      <header class="chat-header">
        <div class="title-zone">
          <el-button v-if="isMobile" text :icon="Fold" @click="sessionDrawerVisible = true" />
          <div>
            <p class="page-kicker">Chat Workspace</p>
            <h1>{{ currentTitle }}</h1>
          </div>
        </div>
        <div class="toolbar">
          <el-tag :type="store.isStreaming ? 'warning' : 'success'">
            {{ store.isStreaming ? '生成中' : '就绪' }}
          </el-tag>
          <el-select
            class="model-selector"
            :model-value="store.selectedModelId"
            :loading="store.isLoadingChatModels"
            :disabled="store.isStreaming"
            filterable
            placeholder="选择模型"
            @change="(value: string) => store.setSelectedModel(value)"
          >
            <el-option
              v-for="model in store.chatModels"
              :key="model.id"
              :label="`${model.provider} / ${model.name}`"
              :value="model.id"
            >
              <span>{{ model.provider }} / {{ model.name }}</span>
              <span class="model-option-meta">{{ model.contextWindowTokens }} / {{ model.maxOutputTokens }}</span>
            </el-option>
          </el-select>
          <el-button :icon="Refresh" @click="store.currentSessionId && store.selectSession(store.currentSessionId, true)">
            刷新
          </el-button>
          <el-button
            v-if="isMobile"
            :icon="Operation"
            :type="agentPanelVisible ? 'primary' : 'default'"
            @click="agentPanelVisible = !agentPanelVisible"
          >
            Agent 工作台
          </el-button>
        </div>
      </header>

      <div class="chat-status-strip">
        <div v-for="item in agentStageCards" :key="item.label" class="status-tile">
          <span>{{ item.label }}</span>
          <el-tag :type="item.type">{{ item.value }}</el-tag>
        </div>
        <div class="status-tile">
          <span>草稿/正式</span>
          <strong>{{ draftArtifactCount }}/{{ finalArtifactCount }}</strong>
        </div>
      </div>

      <div ref="scrollContainer" class="message-viewport">
        <el-alert
          v-if="store.errorMessage"
          :title="store.errorMessage"
          type="error"
          show-icon
          :closable="false"
        />

        <el-skeleton v-if="store.isLoadingHistory" :rows="5" animated />

        <section v-if="store.currentMessages.length === 0 && !store.isLoadingHistory" class="empty-chat">
          <h2>开始一次只读分析</h2>
          <p>选择一个真实工作场景，或直接输入设备、日志、配方、产能、知识库问题。</p>
          <div class="suggestions">
            <button v-for="item in suggestions" :key="item" type="button" @click="useSuggestion(item)">
              {{ item }}
            </button>
          </div>
        </section>

        <div class="message-list">
          <MessageItem v-for="message in store.currentMessages" :key="message.timestamp" :message="message" />
        </div>
      </div>

      <footer class="composer">
        <el-input
          v-model="inputValue"
          type="textarea"
          :autosize="{ minRows: 1, maxRows: 5 }"
          :disabled="isInputDisabled"
          :placeholder="!store.selectedModelId ? '没有可用对话模型' : store.isWaitingForApproval ? '请先处理待审批请求' : '输入问题，Enter 发送，Shift + Enter 换行'"
          @keydown.enter.prevent="(event: KeyboardEvent) => { if (!event.shiftKey) sendMessage() }"
        />
        <el-button
          type="primary"
          :icon="Promotion"
          :disabled="isInputDisabled || !inputValue.trim()"
          @click="sendMessage"
        />
      </footer>
    </section>

    <aside class="context-panel" :class="{ 'mobile-open': isMobile && agentPanelVisible }">
      <section class="panel command-panel">
        <div class="panel-header">
          <div>
            <h2 class="panel-title">Agent 工作台</h2>
            <p class="panel-subtitle">计划、审批、产物和审计集中处理</p>
          </div>
          <el-icon><Operation /></el-icon>
        </div>
        <div class="panel-body command-body">
          <input ref="fileInput" class="hidden-file" type="file" @change="handleFileChange">
          <div class="command-metrics">
            <div>
              <span>步骤</span>
              <strong>{{ completedStepCount }}/{{ taskSteps.length }}</strong>
            </div>
            <div>
              <span>工作区文件</span>
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
          <div class="upload-list" v-if="store.uploadedFiles.length">
            <span v-for="file in store.uploadedFiles.slice(0, 3)" :key="file.id">
              {{ file.fileName }}
            </span>
          </div>
          <div class="command-actions">
            <el-button :icon="Upload" :loading="store.isAgentBusy" @click="openFilePicker">
              上传输入
            </el-button>
            <el-button :icon="Refresh" :disabled="!latestTask" :loading="store.isAgentBusy" @click="continueLatestTask">
              继续任务
            </el-button>
          </div>
          <el-input
            v-model="agentGoal"
            type="textarea"
            :autosize="{ minRows: 2, maxRows: 4 }"
            placeholder="输入 Agent 任务目标"
          />
          <div class="agent-actions">
            <el-button type="primary" :loading="store.isAgentBusy" :disabled="!canCreatePlan" @click="createAgentPlan">
              生成计划
            </el-button>
            <el-button
              :disabled="!canApprovePlan"
              :loading="store.isAgentBusy"
              @click="approveAndRunLatestTask"
            >
              确认并运行
            </el-button>
          </div>
          <el-alert
            v-if="store.agentErrorMessage"
            :title="store.agentErrorMessage"
            type="error"
            :closable="false"
          />
        </div>
      </section>

      <section class="panel">
        <div class="panel-header">
          <div>
            <h2 class="panel-title">审批队列</h2>
            <p class="panel-subtitle">计划、工具、产物和最终输出统一审批</p>
          </div>
          <el-tag :type="pendingAgentApprovals.length ? 'warning' : 'success'">
            {{ pendingAgentApprovals.length }}
          </el-tag>
        </div>
        <div class="panel-body approval-queue">
          <template v-if="approvalGroups.length">
            <div v-for="group in approvalGroups" :key="group.label" class="approval-group">
              <div class="group-title">{{ group.label }}</div>
              <div v-for="approval in group.approvals" :key="approval.id" class="approval-row">
                <div class="approval-main">
                  <span>{{ approval.targetName }}</span>
                  <small>{{ approval.riskLevel }} · {{ approval.reason || '等待确认' }}</small>
                </div>
                <div class="approval-actions">
                  <el-button text type="primary" :loading="store.isAgentBusy" @click="approveAgentApproval(approval.id)">
                    批准
                  </el-button>
                  <el-button text type="danger" :loading="store.isAgentBusy" @click="rejectAgentApproval(approval.id)">
                    驳回
                  </el-button>
                </div>
              </div>
            </div>
          </template>
          <el-empty v-else :image-size="48" description="暂无待审批项" />
        </div>
      </section>

      <section class="panel">
        <div class="panel-header">
          <div>
            <h2 class="panel-title">任务步骤</h2>
            <p class="panel-subtitle">{{ latestTask?.title || '尚未生成 Agent 任务' }}</p>
          </div>
          <el-tag :type="taskStatus.type">{{ taskStatus.label }}</el-tag>
        </div>
        <div class="panel-body task-card">
          <el-alert
            v-if="latestTask?.lastFailureReason"
            :title="latestTask.lastFailureReason"
            type="error"
            show-icon
            :closable="false"
          />
          <div class="step-list" v-if="taskSteps.length">
            <div v-for="step in taskSteps" :key="step.id" class="step-row">
              <i class="step-index">{{ step.stepIndex }}</i>
              <div class="step-main">
                <span>{{ step.title }}</span>
                <small v-if="step.toolCode">{{ step.toolCode }}</small>
                <small v-if="step.errorMessage" class="step-error">{{ step.errorMessage }}</small>
              </div>
              <div class="step-status">
                <el-tag size="small" :type="step.status === 'Completed' ? 'success' : step.requiresApproval ? 'warning' : 'info'">
                  {{ step.status }}
                </el-tag>
              </div>
            </div>
          </div>
          <el-empty v-else :image-size="48" description="暂无步骤" />
          <el-button
            v-if="latestTask?.canRetry"
            plain
            :loading="store.isAgentBusy"
            :disabled="!canContinueTask"
            @click="continueLatestTask"
          >
            继续执行
          </el-button>
        </div>
      </section>

      <section v-if="chartBars.length || taskArtifacts.length" class="panel">
        <div class="panel-header">
          <div>
            <h2 class="panel-title">产物与预览</h2>
            <p class="panel-subtitle">{{ workspaceStatus.label }} · {{ store.currentWorkspace?.workspaceCode || '未创建工作区' }}</p>
          </div>
          <el-icon><Files /></el-icon>
        </div>
        <div class="panel-body artifact-workspace">
          <div v-if="chartBars.length" class="chart-preview">
            <div class="chart-preview-head">
              <span>图表预览</span>
              <small>{{ store.chartPreview?.source || 'workspace' }}</small>
            </div>
            <div v-for="bar in chartBars" :key="bar.label" class="chart-bar-row">
              <span>{{ bar.label }}</span>
              <div class="chart-bar-track">
                <i :style="{ width: bar.width }" />
              </div>
              <strong>{{ bar.value }}</strong>
            </div>
          </div>
          <div v-for="group in artifactGroups" :key="group.label" class="artifact-group">
            <div class="group-title">{{ group.label }}</div>
            <div v-for="artifact in group.artifacts" :key="artifact.id" class="artifact-row">
              <div class="artifact-main">
                <span>{{ artifact.name }}</span>
                <small>
                  v{{ artifact.version }} · step {{ artifact.generatedByStepOrder ?? '-' }} · {{ artifact.approvalStatus || artifact.status }}
                </small>
              </div>
              <div class="artifact-actions">
                <el-tag size="small">{{ artifact.status }}</el-tag>
                <el-button text :icon="Download" @click="downloadArtifact(artifact.id)" />
              </div>
            </div>
          </div>
          <el-button
            type="primary"
            plain
            :loading="store.isAgentBusy"
            :disabled="!canFinalizeWorkspace"
            @click="finalizeCurrentWorkspace"
          >
            确认正式输出
          </el-button>
        </div>
      </section>

      <section v-if="latestTask" class="panel">
        <div class="panel-header">
          <div>
            <h2 class="panel-title">审计摘要</h2>
            <p class="panel-subtitle">任务动作、结果和失败原因</p>
          </div>
          <el-button text :icon="Refresh" :loading="store.isAgentBusy" @click="refreshAuditSummary">
            刷新
          </el-button>
        </div>
        <div class="panel-body audit-list">
          <div v-if="auditSummary.length" class="audit-items">
            <div v-for="item in auditSummary" :key="item.id" class="audit-row">
              <div class="audit-main">
                <span>{{ item.actionCode }}</span>
                <small>{{ item.summary }}</small>
              </div>
              <el-tag size="small" :type="item.result === 'Succeeded' ? 'success' : 'danger'">
                {{ item.result }}
              </el-tag>
            </div>
          </div>
          <el-empty v-else :image-size="48" description="暂无审计记录" />
        </div>
      </section>

      <section class="panel">
        <div class="panel-header">
          <div>
            <h2 class="panel-title">运行边界</h2>
            <p class="panel-subtitle">Cloud 只读，危险动作必须审批</p>
          </div>
          <el-icon><Warning /></el-icon>
        </div>
        <div class="panel-body boundary-list">
          <div>
            <span>现场确认</span>
            <el-tag :type="onsiteStatus.type">{{ onsiteStatus.label }}</el-tag>
          </div>
          <div>
            <span>登录来源</span>
            <el-tag type="info">{{ loginSource }}</el-tag>
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
          <el-button type="primary" plain :icon="Connection" @click="store.confirmOnsitePresence(30)">
            确认在岗 30 分钟
          </el-button>
          <el-button @click="store.clearOnsitePresence()">清除在岗声明</el-button>
        </div>
      </section>

      <section class="panel">
        <div class="panel-header">
          <div>
            <h2 class="panel-title">数据来源</h2>
            <p class="panel-subtitle">正式业务数据和补充分析分离</p>
          </div>
          <el-icon><Document /></el-icon>
        </div>
        <div class="panel-body source-list">
          <span>Cloud 业务数据只读分析，不写回主数据</span>
          <span>DataAnalysis 只读查询用于图表、统计和解释</span>
          <span>工作区只允许 source/data/charts/draft/final/logs/audit 固定目录</span>
          <span>正式产物必须通过最终确认进入 final/</span>
        </div>
      </section>
    </aside>

    <el-drawer v-model="sessionDrawerVisible" size="320px" direction="ltr" title="会话">
      <SessionList />
    </el-drawer>
  </div>
</template>

<style scoped>
.workspace {
  display: grid;
  grid-template-columns: 300px minmax(0, 1fr) 380px;
  height: 100%;
  min-height: 0;
  border: 1px solid var(--app-border);
  border-radius: 24px;
  background: #eef2f6;
  box-shadow: var(--shadow-sm);
  overflow: hidden;
}

.workbench-rail {
  display: grid;
  grid-template-rows: minmax(0, 1fr) auto;
  min-height: 0;
  border-right: 1px solid var(--app-border);
  background: #f7f9fc;
}

.sessions {
  min-height: 0;
}

.rail-task-history {
  display: grid;
  gap: 10px;
  border-top: 1px solid var(--app-border);
  padding: 14px;
  background: #ffffff;
}

.rail-task-history header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.rail-task-history h2 {
  margin: 0;
  font-size: 15px;
  font-weight: 800;
}

.rail-task-history span,
.rail-empty {
  color: var(--app-text-muted);
  font-size: 12px;
}

.rail-task-list {
  display: grid;
  gap: 8px;
  max-height: 220px;
  overflow-y: auto;
}

.rail-task-item {
  display: grid;
  gap: 4px;
  border: 1px solid var(--app-border);
  border-radius: 14px;
  padding: 10px;
  background: #ffffff;
  color: var(--app-text);
  cursor: pointer;
  text-align: left;
}

.rail-task-item.active {
  border-color: var(--app-primary);
  box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.08);
}

.rail-task-item strong,
.rail-task-item span {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.chat-main {
  display: flex;
  min-width: 0;
  min-height: 0;
  flex-direction: column;
  background: #ffffff;
}

.chat-header {
  display: flex;
  min-height: 76px;
  flex-shrink: 0;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  border-bottom: 1px solid var(--app-border);
  padding: 14px 18px;
}

.title-zone,
.toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
}

.chat-header h1 {
  margin: 0;
  font-size: 21px;
  font-weight: 800;
}

.model-selector {
  width: 240px;
}

.model-option-meta {
  float: right;
  margin-left: 18px;
  color: var(--app-text-muted);
  font-size: 12px;
}

.chat-status-strip {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 10px;
  flex-shrink: 0;
  border-bottom: 1px solid var(--app-border);
  padding: 12px 18px;
  background: #f8fafc;
}

.status-tile {
  display: flex;
  min-width: 0;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  border: 1px solid var(--app-border);
  border-radius: 14px;
  padding: 9px 10px;
  background: #ffffff;
}

.status-tile span {
  overflow: hidden;
  color: var(--app-text-muted);
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.message-viewport {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 20px;
  background: #f3f6fa;
}

.message-list {
  display: grid;
  gap: 14px;
  max-width: 960px;
  margin: 0 auto;
}

.empty-chat {
  display: grid;
  gap: 14px;
  max-width: 760px;
  margin: 42px auto;
  border: 1px solid var(--app-border);
  border-radius: 22px;
  padding: 24px;
  background: #ffffff;
  box-shadow: var(--shadow-sm);
}

.empty-chat h2 {
  margin: 0;
  font-size: 22px;
  font-weight: 800;
}

.empty-chat p {
  margin: 0;
  color: var(--app-text-muted);
}

.suggestions {
  display: grid;
  gap: 8px;
}

.suggestions button {
  border: 1px solid var(--app-border);
  border-radius: 14px;
  padding: 11px 12px;
  background: #f8fafc;
  color: var(--app-text);
  cursor: pointer;
  text-align: left;
  transition: border-color 0.2s ease, background-color 0.2s ease, color 0.2s ease, box-shadow 0.2s ease;
}

.suggestions button:hover {
  border-color: var(--app-primary);
  background: #ffffff;
  color: var(--app-primary-strong);
  box-shadow: var(--shadow-sm);
}

.composer {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 44px;
  gap: 10px;
  flex-shrink: 0;
  border-top: 1px solid var(--app-border);
  padding: 14px 18px;
  background: #ffffff;
}

.context-panel {
  display: grid;
  align-content: start;
  gap: 14px;
  min-width: 0;
  overflow-y: auto;
  border-left: 1px solid var(--app-border);
  background: #eef2f6;
  padding: 14px;
}

.panel {
  display: grid;
  gap: 12px;
  border: 1px solid rgba(15, 23, 42, 0.08);
  border-radius: 20px;
  padding: 16px;
  background: #ffffff;
  box-shadow: 0 1px 3px rgba(15, 23, 42, 0.04), 0 8px 24px rgba(15, 23, 42, 0.05);
}

.panel-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.panel-title {
  margin: 0;
  font-size: 16px;
  font-weight: 800;
}

.panel-subtitle {
  margin: 3px 0 0;
  color: var(--app-text-muted);
  font-size: 12px;
}

.panel-body,
.command-body,
.approval-queue,
.artifact-workspace,
.audit-list,
.boundary-list,
.source-list {
  display: grid;
  gap: 10px;
}

.hidden-file {
  display: none;
}

.command-metrics {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
}

.command-metrics div,
.blocked-step {
  display: grid;
  gap: 4px;
  border: 1px solid var(--app-border);
  border-radius: 14px;
  padding: 10px;
  background: #f8fafc;
}

.command-metrics span,
.blocked-step span {
  color: var(--app-text-muted);
  font-size: 12px;
}

.command-metrics strong {
  font-size: 20px;
  font-variant-numeric: tabular-nums;
}

.command-actions,
.agent-actions {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
}

.upload-list,
.step-list,
.artifact-group,
.approval-group,
.audit-items {
  display: grid;
  gap: 8px;
}

.upload-list span {
  overflow: hidden;
  border: 1px solid var(--app-border);
  border-radius: 12px;
  padding: 7px 9px;
  background: #f8fafc;
  color: var(--app-text-muted);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.group-title {
  color: var(--app-text-muted);
  font-size: 12px;
  font-weight: 700;
}

.step-row {
  display: grid;
  grid-template-columns: 28px minmax(0, 1fr) auto;
  align-items: center;
  gap: 8px;
}

.step-index {
  display: grid;
  width: 26px;
  height: 26px;
  place-items: center;
  border-radius: 999px;
  background: #eef2ff;
  color: var(--app-primary-strong);
  font-size: 12px;
  font-style: normal;
  font-weight: 800;
}

.artifact-row,
.approval-row,
.audit-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  border: 1px solid var(--app-border);
  border-radius: 14px;
  padding: 10px;
  background: #ffffff;
}

.approval-row {
  background: var(--el-color-warning-light-9);
}

.artifact-main,
.approval-main,
.audit-main,
.step-main {
  display: grid;
  min-width: 0;
  gap: 2px;
}

.step-main span,
.artifact-main span,
.approval-main span,
.audit-main span {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.artifact-main small,
.approval-main small,
.audit-main small,
.step-main small,
.chart-preview-head small {
  color: var(--app-text-muted);
}

.step-error {
  color: var(--el-color-danger);
}

.artifact-actions,
.approval-actions,
.step-status {
  display: flex;
  flex-shrink: 0;
  align-items: center;
  gap: 4px;
}

.chart-preview {
  display: grid;
  gap: 8px;
  border: 1px solid var(--app-border);
  border-radius: 14px;
  padding: 10px;
  background: #f8fafc;
}

.chart-preview-head,
.chart-bar-row {
  display: grid;
  grid-template-columns: minmax(0, 96px) minmax(0, 1fr) auto;
  align-items: center;
  gap: 8px;
}

.chart-preview-head {
  grid-template-columns: minmax(0, 1fr) auto;
  font-weight: 700;
}

.chart-bar-row span {
  overflow: hidden;
  color: var(--app-text-muted);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.chart-bar-track {
  height: 8px;
  overflow: hidden;
  border-radius: 999px;
  background: var(--app-border);
}

.chart-bar-track i {
  display: block;
  height: 100%;
  border-radius: inherit;
  background: var(--app-primary);
}

.boundary-list > div {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.boundary-list span,
.source-list span {
  color: var(--app-text-muted);
}

.boundary-list strong {
  font-size: 20px;
  font-variant-numeric: tabular-nums;
}

.source-list span {
  border-left: 3px solid var(--app-primary);
  padding-left: 8px;
}

@media (max-width: 1360px) {
  .workspace {
    grid-template-columns: 280px minmax(0, 1fr) 340px;
  }
}

@media (max-width: 1180px) {
  .workspace {
    grid-template-columns: 270px minmax(0, 1fr);
  }

  .context-panel {
    display: none;
  }

  .context-panel.mobile-open {
    display: grid;
    border-top: 1px solid var(--app-border);
    border-left: 0;
  }
}

@media (max-width: 980px) {
  .workspace {
    grid-template-columns: 1fr;
    height: auto;
    min-height: calc(100vh - 170px);
  }

  .message-viewport {
    min-height: 55vh;
  }

  .chat-header,
  .toolbar {
    align-items: stretch;
    flex-direction: column;
  }

  .model-selector {
    width: 100%;
  }

  .chat-status-strip {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .context-panel.mobile-open {
    max-height: none;
    overflow-y: visible;
  }
}
</style>
