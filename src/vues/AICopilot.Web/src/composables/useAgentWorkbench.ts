import { computed } from 'vue'
import { useAuthStore } from '@/stores/authStore'
import { useChatStore } from '@/stores/chatStore'
import type { AgentApprovalRequest, AgentStep, AgentTask, ArtifactRecord } from '@/types/protocols'

type TagType = 'success' | 'warning' | 'info' | 'danger' | 'primary'
type AgentRunNoticeTone = 'success' | 'warning' | 'danger'

export type AgentRunNotice = {
  tone: AgentRunNoticeTone
  title: string
  detail: string
}

function statusLabel(status?: string | null) {
  const labels: Record<string, string> = {
    Draft: '计划草案',
    Planned: '已规划',
    WaitingPlanApproval: '待确认计划',
    WaitingApproval: '待审批',
    Running: '执行中',
    PlanApproved: '计划已批准',
    WorkspaceReady: '工作区就绪',
    WaitingFinalApproval: '等待最终审批',
    Completed: '已完成',
    Failed: '失败',
    Cancelled: '已取消',
    Rejected: '已驳回',
    Finalized: '已正式输出',
    ReadyForFinalize: '待最终确认',
    Pending: '待审批',
    Approved: '已批准',
    Succeeded: '成功'
  }

  return status ? labels[status] ?? status : '-'
}

function statusType(status?: string | null): TagType {
  switch (status) {
    case 'Completed':
    case 'Finalized':
    case 'Approved':
    case 'Succeeded':
      return 'success'
    case 'WaitingPlanApproval':
    case 'Draft':
    case 'WaitingApproval':
    case 'ReadyForFinalize':
    case 'Pending':
    case 'Running':
      return 'warning'
    case 'Failed':
    case 'Cancelled':
    case 'Rejected':
      return 'danger'
    default:
      return 'info'
  }
}

function approvalKindLabel(type?: string | null) {
  const labels: Record<string, string> = {
    Plan: '计划确认',
    Tool: '执行确认',
    Artifact: '产物确认',
    FinalOutput: '最终输出'
  }

  return type ? labels[type] ?? type : '审批'
}

function artifactKindLabel(type?: string | null, previewKind?: string | null) {
  const key = (previewKind || type || '').toLowerCase()
  if (key.includes('markdown')) return 'Markdown'
  if (key.includes('html')) return 'HTML'
  if (key.includes('chart')) return '图表数据'
  if (key.includes('spreadsheet') || key.includes('xlsx')) return 'XLSX'
  if (key.includes('pdf')) return 'PDF'
  if (key.includes('ppt')) return 'PPTX'
  if (key.includes('json')) return 'JSON'
  if (key.includes('csv') || key.includes('table')) return '表格数据'
  return type || previewKind || '产物'
}

function stepStatusRank(step: AgentStep) {
  if (step.status === 'Running') return 0
  if (step.status === 'WaitingApproval') return 1
  if (step.status === 'Failed') return 2
  if (step.status !== 'Completed') return 3
  return 4
}

export function resolveAgentRunNotice(task?: AgentTask | null): AgentRunNotice | null {
  if (!task) return null

  if (task.isRunQueued) {
    const queueStatus = task.runQueueStatus || 'Queued'
    return {
      tone: queueStatus === 'Leased' ? 'success' : 'warning',
      title: queueStatus === 'Leased' ? 'Worker 已接手执行' : '任务已入队，等待 Worker 接手',
      detail: task.queuedRunId
        ? `队列状态：${queueStatus} · 运行单 ${task.queuedRunId.slice(0, 8)}`
        : `队列状态：${queueStatus}`
    }
  }

  if (task.isRunInProgress || task.status === 'Running' || task.status === 'GeneratingArtifacts') {
    return {
      tone: 'warning',
      title: '正在执行计划步骤',
      detail: '执行进度会自动刷新；需要确认的步骤会停在审批节点。'
    }
  }

  if (task.status === 'Failed' && (task.failureSummary || task.lastFailureReason)) {
    return {
      tone: 'danger',
      title: task.failureSummary?.errorCode || '执行失败',
      detail: task.failureSummary?.safeMessage || task.lastFailureReason || '任务失败，可查看执行记录后重试。'
    }
  }

  return null
}

export function useAgentWorkbench() {
  const store = useChatStore()
  const authStore = useAuthStore()

  const latestTask = computed(() => store.latestAgentTask)
  const taskHistory = computed(() => store.agentTasks.slice(0, 6))
  const taskSteps = computed(() => latestTask.value?.steps ?? [])
  const taskArtifacts = computed(() => store.currentWorkspace?.artifacts ?? [])
  const draftArtifacts = computed(() =>
    store.currentWorkspace?.draftArtifacts?.length
      ? store.currentWorkspace.draftArtifacts
      : taskArtifacts.value.filter((artifact) => (artifact.artifactStatus || artifact.status) !== 'Final')
  )
  const finalArtifacts = computed(() =>
    store.currentWorkspace?.finalArtifacts?.length
      ? store.currentWorkspace.finalArtifacts
      : taskArtifacts.value.filter((artifact) => (artifact.artifactStatus || artifact.status) === 'Final')
  )
  const currentArtifactPreview = computed(() => store.currentArtifactPreview)
  const pendingAgentApprovals = computed(() => store.pendingAgentApprovals)
  const auditSummary = computed(() => store.agentAuditSummary.slice(0, 8))

  const completedStepCount = computed(() =>
    taskSteps.value.filter((step) => step.status === 'Completed').length
  )
  const blockedStep = computed(() =>
    [...taskSteps.value].sort(stepStatusRank).find((step) => step.status !== 'Completed') ?? null
  )
  const workspaceFileCount = computed(() => store.currentWorkspace?.files.length ?? 0)
  const draftArtifactCount = computed(() =>
    draftArtifacts.value.length
  )
  const finalArtifactCount = computed(() =>
    finalArtifacts.value.length
  )

  const taskStatus = computed(() => ({
    label: statusLabel(latestTask.value?.status),
    type: statusType(latestTask.value?.status)
  }))

  const workspaceStatus = computed(() => ({
    label: statusLabel(store.currentWorkspace?.status),
    type: statusType(store.currentWorkspace?.status)
  }))

  const approvalGroups = computed(() => {
    const groups = new Map<string, AgentApprovalRequest[]>()
    for (const approval of pendingAgentApprovals.value) {
      const key = approvalKindLabel(approval.type)
      groups.set(key, [...(groups.get(key) ?? []), approval])
    }

    return Array.from(groups.entries()).map(([label, approvals]) => ({ label, approvals }))
  })

  const artifactGroups = computed(() => {
    const groups = new Map<string, ArtifactRecord[]>()
    for (const artifact of taskArtifacts.value) {
      const key = artifactKindLabel(artifact.type, artifact.previewKind)
      groups.set(key, [...(groups.get(key) ?? []), artifact])
    }

    return Array.from(groups.entries()).map(([label, artifacts]) => ({ label, artifacts }))
  })

  const chartBars = computed(() => {
    const preview = store.chartPreview
    if (!preview || preview.labels.length === 0) return []
    const max = Math.max(...preview.values, 1)
    return preview.labels.slice(0, 6).map((label, index) => ({
      label,
      value: preview.values[index] ?? 0,
      width: `${Math.max(4, ((preview.values[index] ?? 0) / max) * 100)}%`
    }))
  })

  const onsiteStatus = computed(() => {
    const expires = store.currentSession?.onsiteConfirmationExpiresAt
    if (!expires) return { label: '未确认', type: 'info' as const }
    return new Date(expires).getTime() > Date.now()
      ? { label: `有效至 ${new Date(expires).toLocaleTimeString('zh-CN', { hour12: false })}`, type: 'success' as const }
      : { label: '已过期', type: 'warning' as const }
  })

  const agentStageCards = computed(() => [
    {
      label: '任务状态',
      value: taskStatus.value.label,
      type: taskStatus.value.type
    },
    {
      label: '步骤进度',
      value: `${completedStepCount.value}/${taskSteps.value.length || 0}`,
      type: taskSteps.value.length > 0 && completedStepCount.value === taskSteps.value.length ? 'success' as TagType : 'info' as TagType
    },
    {
      label: '待审批',
      value: String(pendingAgentApprovals.value.length),
      type: pendingAgentApprovals.value.length > 0 ? 'warning' as TagType : 'success' as TagType
    },
    {
      label: '工作区',
      value: workspaceStatus.value.label,
      type: workspaceStatus.value.type
    }
  ])

  const approvalCount = computed(() =>
    store.currentMessages.flatMap((message) => message.chunks).filter((chunk) => chunk.type === 'ApprovalRequest').length
  )
  const widgetCount = computed(() =>
    store.currentMessages.flatMap((message) => message.chunks).filter((chunk) => chunk.type === 'Widget').length
  )

  const canCreatePlan = computed(() =>
    Boolean(store.currentSessionId && !store.isAgentBusy)
  )
  const canRunTask = computed(() =>
    Boolean(latestTask.value?.canRun && !latestTask.value?.isRunQueued && !latestTask.value?.isRunInProgress && !store.isAgentBusy)
  )
  const canContinueTask = computed(() =>
    Boolean(latestTask.value?.canRetry && !store.isAgentBusy)
  )
  const canSubmitFinalReview = computed(() =>
    Boolean(latestTask.value?.canSubmitFinalReview && !store.isAgentBusy)
  )
  const canFinalizeWorkspace = computed(() =>
    Boolean(latestTask.value?.canApproveFinal && store.currentWorkspace && store.currentWorkspace.status !== 'Finalized' && taskArtifacts.value.length > 0 && !store.isAgentBusy)
  )
  const agentRunNotice = computed(() => resolveAgentRunNotice(latestTask.value))

  return {
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
    agentRunNotice,
    approvalCount,
    widgetCount,
    canCreatePlan,
    canRunTask,
    canContinueTask,
    canSubmitFinalReview,
    canFinalizeWorkspace,
    loginSource: computed(() => authStore.loginSource),
    cloudEmployeeNo: computed(() => authStore.cloudEmployeeNo)
  }
}
