<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import {
  Check,
  FolderOpen,
  ListChecks,
  MessageCircle,
  Play,
  RefreshCw,
  ShieldCheck,
  Sparkles,
  TriangleAlert,
  X,
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
  latestPlanKindLabel,
  latestPlanTopologyProfile,
  latestPlanIsCloudReadonly,
  latestPlanIsSimulation,
  isPlanDraftTask,
  isPlanConfirmable,
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
const visibleTaskResult = computed(() => {
  const candidates = [...(store.agentRuntimeSnapshot?.evidence ?? [])]
    .reverse()
    .filter((item) =>
      item.evidenceKind !== 'ArtifactReference' &&
      ['ObservedFact', 'DerivedFact', 'ModelPrediction', 'LlmInference', 'Recommendation'].includes(
        item.truthClass,
      ),
    )
  const evidence = candidates.find((item) => item.findings.length > 0) ?? candidates[0]
  if (latestTask.value?.finalSummary) {
    return {
      title: '最终结果',
      summary: latestTask.value.finalSummary,
      truthLabel: evidence?.truthLabel ?? '历史结果（真值类型未记录）',
    }
  }

  if (!['WorkspaceReady', 'WaitingFinalApproval', 'Completed'].includes(latestTask.value?.status ?? '')) {
    return null
  }

  return evidence
    ? { title: '执行结果', summary: evidence.safeSummary, truthLabel: evidence.truthLabel }
    : null
})
const completedTaskEvidenceDigest = computed(() => {
  const task = latestTask.value
  const snapshot = store.agentRuntimeSnapshot
  if (
    !task ||
    task.status !== 'Completed' ||
    snapshot?.taskId !== task.id ||
    !snapshot.nodes.some(
      (node) => node.kind === 'ApprovalCheckpointNode' && node.status === 'Succeeded',
    ) ||
    !snapshot.evidenceSetDigest
  ) {
    return null
  }

  return snapshot.evidenceSetDigest
})
const canReferenceTaskResult = computed(() =>
  Boolean(visibleTaskResult.value && completedTaskEvidenceDigest.value),
)
const isTaskResultReferenced = computed(() =>
  Boolean(latestTask.value?.id && store.referencedAgentTaskId === latestTask.value.id),
)
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

  if (task.status === 'WaitingToolApproval') {
    return '执行已在审批点暂停，确认前不会继续调用工具或 Cloud 只读能力。'
  }

  if (task.status === 'Failed') {
    return '执行已停止，错误原因会显示在当前对话块里，重试会基于已确认计划继续。'
  }

  if (task.status === 'Rejected') {
    return '计划已拒绝，未进入工具调用、Cloud 查询或 Worker 执行。'
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
const canCancelTask = computed(() => {
  const task = latestTask.value
  if (!task || isPlanDraftTask.value || store.isAgentBusy) return false

  return Boolean(
    task.isRunQueued ||
    task.isRunInProgress ||
    ['PlanApproved', 'WaitingToolApproval', 'Running', 'GeneratingArtifacts'].includes(task.status),
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
        disabled: store.isAgentBusy || !isPlanConfirmable.value,
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

async function rejectCurrentPlan() {
  const task = latestTask.value
  if (!task || !isPlanDraftTask.value || store.isAgentBusy) return
  await store.rejectAgentTaskPlan(task.id)
}

async function cancelCurrentTask() {
  const task = latestTask.value
  if (!task || !canCancelTask.value) return
  if (!window.confirm('确认取消当前任务？已持久化的运行记录和证据会保留用于审计。')) return
  await store.cancelAgentTask(task.id)
}

async function previewArtifact(artifactId: string) {
  if (!store.resolvedSessionId || store.isSessionTransitionBlocked) return
  await store.loadArtifactPreview(artifactId)
}

function referenceCurrentTaskResult() {
  const task = latestTask.value
  const digest = completedTaskEvidenceDigest.value
  if (!task || !digest || !canReferenceTaskResult.value) return
  store.referenceAgentTaskForFollowUp(task.id, digest)
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
          拓扑：{{ latestPlanTopologyProfile || '待生成' }}
        </span>
        <span>
          <ShieldCheck :size="14" />
          {{ latestPlanIsSimulation ? 'Simulation · AI 独立模拟业务库' : latestPlanIsCloudReadonly ? 'Cloud 只读' : '只读分析' }}
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
        <button v-if="isPlanDraftTask" type="button" :disabled="store.isAgentBusy" @click="rejectCurrentPlan">
          <X :size="17" />
          拒绝计划
        </button>
        <button v-else-if="canCancelTask" type="button" :disabled="store.isAgentBusy" @click="cancelCurrentTask">
          <X :size="17" />
          取消任务
        </button>
      </div>

      <div
        v-if="visibleTaskResult"
        class="agent-run-notice notice-success"
        data-testid="agent-visible-final-result"
      >
        <Check :size="15" />
        <span>
          <strong>{{ visibleTaskResult.title }}</strong>
          <small>结果类型：{{ visibleTaskResult.truthLabel }}</small>
          <small>{{ visibleTaskResult.summary }}</small>
        </span>
      </div>

      <div v-if="canReferenceTaskResult" class="run-actions">
        <button
          type="button"
          :disabled="isTaskResultReferenced || store.isAgentBusy"
          @click="referenceCurrentTaskResult"
        >
          <MessageCircle :size="17" />
          {{ isTaskResultReferenced ? '已关联到输入框' : '基于此结果追问' }}
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
