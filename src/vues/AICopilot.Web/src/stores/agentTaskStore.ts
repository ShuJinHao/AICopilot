import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { chatService } from '@/services/chatService'
import type {
  AgentApprovalRequest,
  AgentTask,
  AgentTaskAuditSummary
} from '@/types/protocols'
import type { SessionTimelineEvent } from '@/types/app'

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

  async function loadAgentTasks(sessionId: string) {
    try {
      agentTasks.value = await chatService.getAgentTasksBySession(sessionId)
      return agentTasks.value[0] ?? null
    } catch {
      agentTasks.value = []
      return null
    }
  }

  async function loadTimeline(sessionId: string) {
    try {
      const timeline = await chatService.getTimeline(sessionId)
      timelineEvents.value = timeline.items
    } catch {
      timelineEvents.value = []
    }
  }

  async function loadAgentApprovals(taskId: string | null = null) {
    if (!taskId) {
      agentApprovals.value = []
      return
    }

    try {
      agentApprovals.value = await chatService.getAgentTaskApprovals(taskId)
    } catch {
      agentApprovals.value = []
    }
  }

  async function loadAgentAuditSummary(taskId: string | null = null) {
    if (!taskId) {
      agentAuditSummary.value = []
      return
    }

    try {
      agentAuditSummary.value = await chatService.getAgentTaskAuditSummary(taskId)
    } catch {
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
