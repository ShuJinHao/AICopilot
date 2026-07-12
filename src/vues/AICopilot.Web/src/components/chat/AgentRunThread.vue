<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import {
  Check,
  FolderOpen,
  ListChecks,
  Play,
  RefreshCw,
  ShieldCheck,
  Sparkles,
  TriangleAlert,
  Wrench,
} from 'lucide-vue-next'
import AiTag from '@/components/ai/AiTag.vue'
import { useAgentWorkbench } from '@/composables/useAgentWorkbench'
import { useAgentPlanPreview, statusTone } from '@/composables/useAgentPlanPreview'
import { useAgentTimelineDisplay } from '@/composables/useAgentTimelineDisplay'
import { useChatStore } from '@/stores/chatStore'
import AgentApprovalPanel from './AgentApprovalPanel.vue'
import PlanDraftCard from './PlanDraftCard.vue'
import RuntimeDetailsPanel from './RuntimeDetailsPanel.vue'

type PrimaryTaskActionKind =
  | 'approve-and-run'
  | 'run'
  | 'retry'
  | 'submit-final-review'
  | 'finalize'
  | 'queued'
  | 'running'

const store = useChatStore()
const {
  latestTask,
  taskSteps,
  taskArtifacts,
  completedStepCount,
  taskStatus,
  workspaceStatus,
  pendingAgentApprovals,
  approvalGroups,
  agentRunNotice,
  canRunTask,
  canContinueTask,
  canSubmitFinalReview,
  canFinalizeWorkspace,
} = useAgentWorkbench()
const {
  latestPlan,
  latestPlanKindLabel,
  latestPlanVisibleToolCount,
  latestPlanIsCloudReadonly,
  isPlanDraftTask,
} = useAgentPlanPreview()
const { latestTimelineSummary, timelineEventCount, timelineEventItems } = useAgentTimelineDisplay()

const runtimePollingTimer = ref<number | null>(null)
const runtimePollingTaskId = ref<string | null>(null)
const compactOutputArtifacts = computed(() => taskArtifacts.value.slice(0, 3))
const hiddenOutputArtifactCount = computed(() =>
  Math.max(0, taskArtifacts.value.length - compactOutputArtifacts.value.length),
)
const visibleOutputArtifacts = computed(() =>
  isPlanDraftTask.value ? [] : compactOutputArtifacts.value,
)
const hiddenVisibleOutputArtifactCount = computed(() =>
  isPlanDraftTask.value ? 0 : hiddenOutputArtifactCount.value,
)
const selectedPlanTypeLabel = computed(() => store.selectedSkill?.displayName || '自动识别')
const hasRuntimeDetails = computed(() =>
  Boolean(
    latestTask.value ||
    taskSteps.value.length ||
    timelineEventItems.value.length ||
    taskArtifacts.value.length ||
    approvalGroups.value.length,
  ),
)
const inlineRunSubtitle = computed(() => {
  if (isPlanDraftTask.value) {
    return '等待确认 · 未执行'
  }

  if (store.currentWorkspace) {
    return `${workspaceStatus.value.label} · ${taskArtifacts.value.length} 个产物`
  }

  if (latestTask.value?.workspaceCode) {
    return '执行上下文已建立'
  }

  return '目标和计划将随对话推进'
})
const agentRunReply = computed(() => {
  const task = latestTask.value
  if (store.errorMessage) {
    return '这次请求没有进入执行，我保留了当前上下文，下面给出后端返回的原因。'
  }

  if (!task) {
    return '我会先理解目标，再根据模式决定是直接回答还是给出计划草案。'
  }

  if (task.status === 'Draft' || task.status === 'WaitingPlanApproval') {
    return `我已整理出${latestPlanKindLabel.value}，确认前不会执行 Cloud 查询、工具调用或 Worker 任务。`
  }

  if (task.isRunQueued) {
    return '计划已确认，任务正在等待 Worker 执行。'
  }

  if (task.isRunInProgress || task.status === 'Running' || task.status === 'GeneratingArtifacts') {
    return `正在按计划执行，当前进度 ${completedStepCount.value}/${taskSteps.value.length}。`
  }

  if (task.status === 'Failed') {
    return '执行已停止，错误原因会显示在当前对话块里，重试会基于已确认计划继续。'
  }

  if (task.status === 'WorkspaceReady' || task.status === 'WaitingFinalApproval') {
    return '执行结果已生成，待确认后形成正式输出。'
  }

  return inlineRunSubtitle.value
})
const shouldPollRuntimeSnapshot = computed(() => {
  const task = latestTask.value
  if (!task) return false

  return Boolean(
    task.isRunQueued ||
    task.isRunInProgress ||
    task.status === 'Running' ||
    task.status === 'GeneratingArtifacts',
  )
})
const primaryTaskAction = computed(() => {
  const task = latestTask.value
  if (!task) return null

  if (task.isRunQueued) {
    return {
      kind: 'queued' as PrimaryTaskActionKind,
      label: '已入队',
      icon: RefreshCw,
      disabled: true,
    }
  }

  if (task.isRunInProgress || task.status === 'Running' || task.status === 'GeneratingArtifacts') {
    return {
      kind: 'running' as PrimaryTaskActionKind,
      label: '执行中',
      icon: RefreshCw,
      disabled: true,
    }
  }

  switch (task.status) {
    case 'Draft':
    case 'WaitingPlanApproval':
      return {
        kind: 'approve-and-run' as PrimaryTaskActionKind,
        label: '确认计划并执行',
        icon: Play,
        disabled: store.isAgentBusy,
      }
    case 'PlanApproved':
      return {
        kind: 'run' as PrimaryTaskActionKind,
        label: '执行',
        icon: Play,
        disabled: !canRunTask.value,
      }
    case 'WaitingToolApproval':
      return {
        kind: 'run' as PrimaryTaskActionKind,
        label: '继续执行',
        icon: Play,
        disabled: !canRunTask.value,
      }
    case 'Failed':
      return {
        kind: 'retry' as PrimaryTaskActionKind,
        label: '重试',
        icon: RefreshCw,
        disabled: !canContinueTask.value,
      }
    case 'WorkspaceReady':
      return {
        kind: 'submit-final-review' as PrimaryTaskActionKind,
        label: '提交最终审核',
        icon: FolderOpen,
        disabled: !canSubmitFinalReview.value,
      }
    case 'WaitingFinalApproval':
      return {
        kind: 'finalize' as PrimaryTaskActionKind,
        label: '确认正式输出',
        icon: Check,
        disabled: !canFinalizeWorkspace.value,
      }
    default:
      return null
  }
})

function stopRuntimePolling() {
  if (runtimePollingTimer.value !== null && typeof window !== 'undefined') {
    window.clearInterval(runtimePollingTimer.value)
  }
  runtimePollingTimer.value = null
  runtimePollingTaskId.value = null
}

function startRuntimePolling(taskId: string) {
  if (typeof window === 'undefined') return
  if (runtimePollingTimer.value !== null && runtimePollingTaskId.value === taskId) return

  stopRuntimePolling()
  runtimePollingTaskId.value = taskId
  runtimePollingTimer.value = window.setInterval(() => {
    if (!store.isAgentBusy) {
      void store.refreshAgentTaskSnapshot(taskId)
    }
  }, 3500)
}

async function submitFinalReview() {
  const code = store.currentWorkspace?.workspaceCode
  if (!code || !canSubmitFinalReview.value) return
  await store.submitFinalReview(code)
}

async function finalizeCurrentWorkspace() {
  const code = store.currentWorkspace?.workspaceCode
  if (!code || !canFinalizeWorkspace.value) return
  if (!window.confirm('确认后会把当前草稿产物写入 final/，作为正式输出。')) return
  await store.finalizeWorkspace(code)
}

async function runPrimaryTaskAction() {
  const task = latestTask.value
  const action = primaryTaskAction.value
  if (!task || !action || action.disabled) return

  switch (action.kind) {
    case 'approve-and-run':
      await store.approveAndRunAgentTask(task.id)
      return
    case 'run':
      await store.runAgentTask(task.id)
      return
    case 'retry':
      await store.retryAgentTask(task.id)
      return
    case 'submit-final-review':
      await submitFinalReview()
      return
    case 'finalize':
      await finalizeCurrentWorkspace()
      return
    default:
      return
  }
}

async function previewArtifact(artifactId: string) {
  if (!store.resolvedSessionId || store.isSessionTransitionBlocked) return
  await store.loadArtifactPreview(artifactId)
}

watch(
  [() => latestTask.value?.id, () => shouldPollRuntimeSnapshot.value],
  ([taskId, shouldPoll]) => {
    if (taskId && shouldPoll) {
      startRuntimePolling(taskId)
      return
    }

    stopRuntimePolling()
  },
  { immediate: true },
)

watch(() => store.currentSessionId, stopRuntimePolling)
onBeforeUnmount(stopRuntimePolling)
</script>

<template>
  <section class="agent-run-thread" data-testid="inline-agent-run">
    <div class="agent-avatar">
      <Sparkles :size="18" />
    </div>
    <article class="agent-run-message">
      <header class="agent-run-header">
        <div>
          <span>AICopilot</span>
          <p>{{ agentRunReply }}</p>
        </div>
        <AiTag :tone="statusTone(taskStatus.type)">{{ taskStatus.label }}</AiTag>
      </header>

      <div class="agent-run-context">
        <span>
          <ListChecks :size="14" />
          {{ latestPlanKindLabel }}
        </span>
        <span>
          <Sparkles :size="14" />
          Skill：{{ latestPlan?.skillName || latestPlan?.skillCode || selectedPlanTypeLabel }}
        </span>
        <span>
          <Wrench :size="14" />
          工具 {{ latestPlanVisibleToolCount }}
        </span>
        <span>
          <ShieldCheck :size="14" />
          {{ latestPlanIsCloudReadonly ? 'Cloud 只读' : '只读分析' }}
        </span>
        <span v-if="timelineEventCount">
          <ListChecks :size="14" />
          {{ latestTimelineSummary }}
        </span>
      </div>

      <div v-if="store.errorMessage" class="canvas-error inline-error" role="alert">
        <TriangleAlert :size="18" />
        {{ store.errorMessage }}
      </div>

      <div v-if="agentRunNotice" class="agent-run-notice" :class="`notice-${agentRunNotice.tone}`">
        <RefreshCw v-if="agentRunNotice.tone !== 'danger'" :size="15" />
        <TriangleAlert v-else :size="15" />
        <span>
          <strong>{{ agentRunNotice.title }}</strong>
          <small>{{ agentRunNotice.detail }}</small>
        </span>
      </div>

      <PlanDraftCard v-if="latestTask" />

      <div v-if="primaryTaskAction" class="run-actions">
        <button type="button" :disabled="primaryTaskAction.disabled" @click="runPrimaryTaskAction">
          <component :is="primaryTaskAction.icon" :size="17" />
          {{ primaryTaskAction.label }}
        </button>
      </div>

      <div v-if="visibleOutputArtifacts.length" class="agent-output-strip" aria-label="输出产物">
        <button
          v-for="artifact in visibleOutputArtifacts"
          :key="artifact.id"
          type="button"
          class="agent-output-pill"
          :disabled="!store.resolvedSessionId || store.isSessionTransitionBlocked"
          @click="previewArtifact(artifact.id)"
        >
          <FolderOpen :size="15" />
          <span>{{ artifact.name }}</span>
        </button>
        <span v-if="hiddenVisibleOutputArtifactCount">+{{ hiddenVisibleOutputArtifactCount }}</span>
      </div>

      <RuntimeDetailsPanel v-if="hasRuntimeDetails" />

      <AgentApprovalPanel v-if="pendingAgentApprovals.length" />
    </article>
  </section>
</template>
