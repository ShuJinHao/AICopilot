import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { chatService } from '@/services/chatService'
import type {
  AgentApprovalRequest,
  AgentTask,
  AgentTaskAuditSummary
} from '@/types/protocols'
import type { SessionTimelineEvent } from '@/types/app'
import { toFriendlyMessage } from './chatErrorStore'

type ErrorReporter = (message: string) => void

function reportLoadError(reportError: ErrorReporter | undefined, action: string, error: unknown) {
  reportError?.(`${action}失败：${toFriendlyMessage(error)}`)
}

export const useAgentTaskStore = defineStore('agentTask', () => {
  const agentTasks = ref<AgentTask[]>([])
  const agentApprovals = ref<AgentApprovalRequest[]>([])
  const agentAuditSummary = ref<AgentTaskAuditSummary[]>([])
  const timelineEvents = ref<SessionTimelineEvent[]>([])
  const isAgentBusy = ref(false)

  const latestAgentTask = computed(() => agentTasks.value[0] ?? null)
  const pendingAgentApprovals = computed(() =>
    agentApprovals.value.filter((approval) => approval.status === 'Pending')
  )

  function reset() {
    agentTasks.value = []
    agentApprovals.value = []
    agentAuditSummary.value = []
    timelineEvents.value = []
    isAgentBusy.value = false
  }

  function upsertAgentTask(task: AgentTask) {
    const index = agentTasks.value.findIndex((item) => item.id === task.id)
    if (index >= 0) {
      agentTasks.value.splice(index, 1, task)
    } else {
      agentTasks.value.unshift(task)
    }
  }

  async function loadAgentTasks(sessionId: string, reportError?: ErrorReporter) {
    try {
      agentTasks.value = await chatService.getAgentTasksBySession(sessionId)
      return agentTasks.value[0] ?? null
    } catch (error) {
      console.error('Failed to load agent tasks for session.', error)
      reportLoadError(reportError, '加载任务状态', error)
      agentTasks.value = []
      return null
    }
  }

  async function loadTimeline(sessionId: string, reportError?: ErrorReporter) {
    try {
      const timeline = await chatService.getTimeline(sessionId)
      timelineEvents.value = timeline.items
    } catch (error) {
      console.error('Failed to load agent task timeline.', error)
      reportLoadError(reportError, '加载任务时间线', error)
      timelineEvents.value = []
    }
  }

  async function loadAgentApprovals(taskId: string | null = null, reportError?: ErrorReporter) {
    if (!taskId) {
      agentApprovals.value = []
      return
    }

    try {
      agentApprovals.value = await chatService.getAgentTaskApprovals(taskId)
    } catch (error) {
      console.error('Failed to load agent task approvals.', error)
      reportLoadError(reportError, '加载审批记录', error)
      agentApprovals.value = []
    }
  }

  async function loadAgentAuditSummary(taskId: string | null = null, reportError?: ErrorReporter) {
    if (!taskId) {
      agentAuditSummary.value = []
      return
    }

    try {
      agentAuditSummary.value = await chatService.getAgentTaskAuditSummary(taskId)
    } catch (error) {
      console.error('Failed to load agent task audit summary.', error)
      reportLoadError(reportError, '加载审计摘要', error)
      agentAuditSummary.value = []
    }
  }

  function findPendingPlanApproval(taskId: string) {
    return agentApprovals.value.find((approval) =>
      approval.taskId === taskId &&
      approval.type === 'Plan' &&
      approval.status === 'Pending'
    ) ?? null
  }

  return {
    agentTasks,
    agentApprovals,
    agentAuditSummary,
    timelineEvents,
    isAgentBusy,
    latestAgentTask,
    pendingAgentApprovals,
    reset,
    upsertAgentTask,
    loadAgentTasks,
    loadTimeline,
    loadAgentApprovals,
    loadAgentAuditSummary,
    findPendingPlanApproval
  }
})
