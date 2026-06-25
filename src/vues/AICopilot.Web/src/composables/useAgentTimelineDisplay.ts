import { computed } from 'vue'
import { useChatStore } from '@/stores/chatStore'
import type { SessionTimelineEvent } from '@/types/app'

type TagTone = 'success' | 'warning' | 'dark' | 'lime' | 'danger' | 'neutral' | 'teal' | 'blue'

function approvalTypeLabel(type?: string | null) {
  if (type === 'Plan') return '计划'
  if (type === 'Tool') return '工具'
  if (type === 'ToolCall') return '工具'
  if (type === 'FinalOutput') return '最终输出'
  if (type === 'Artifact') return '产物'
  return '审批'
}

export function formatTimelineTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return '--:--'
  }

  return date.toLocaleTimeString('zh-CN', {
    hour: '2-digit',
    minute: '2-digit'
  })
}

export function formatTimelineScore(score?: number | null) {
  if (typeof score !== 'number' || Number.isNaN(score)) {
    return '相关度 -'
  }

  return `相关度 ${Math.round(score * 100)}%`
}

function timelineEventTitle(event: SessionTimelineEvent) {
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

function timelineEventSubtitle(event: SessionTimelineEvent) {
  return event.agentStepTitle ||
    event.approvalTargetName ||
    event.artifactName ||
    event.agentTaskTitle ||
    event.workspaceCode ||
    event.agentTaskGoal ||
    '执行事件'
}

function resolveTimelineStatus(event: SessionTimelineEvent) {
  return event.approvalStatus ||
    event.agentStepStatus ||
    event.artifactStatus ||
    event.workspaceStatus ||
    event.agentTaskStatus ||
    'Recorded'
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

export function useAgentTimelineDisplay() {
  const store = useChatStore()

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

  return {
    timelineEventItems,
    timelineEventCount,
    latestTimelineSummary
  }
}
