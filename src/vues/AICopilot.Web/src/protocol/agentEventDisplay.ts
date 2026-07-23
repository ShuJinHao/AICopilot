import type { AgentEventPayload } from '@/types/protocols'
import { resolveChatErrorMessage } from '@/stores/chatErrorStore'

export function formatAgentEventDetail(event: AgentEventPayload) {
  if (event.stage === 'plan_draft_failed') {
    return formatPlanDraftFailure(event)
  }

  switch (event.stage) {
    case 'plan_draft_started':
      return '正在生成计划草案。'
    case 'intent_understanding':
      return '正在理解任务目标。'
    case 'capability_discovery':
      return '正在检查可用能力。'
    case 'plan_draft_ready':
      return '计划草案已生成，等待确认。'
    case 'task_evidence_reused':
      return '已绑定所选已完成任务的封存证据。'
    case 'task_evidence_refresh_required':
      return '本轮范围已变化，将执行新的只读查询。'
    default:
      return event.recoverable
        ? '运行事件已记录，可继续查看后续结果。'
        : '运行状态已更新。'
  }
}

export function formatPlanDraftFailure(event: Pick<AgentEventPayload, 'code'>) {
  const fallback = '计划草案生成失败，请调整目标后重试。'
  const code = event.code?.trim()
  if (!code) {
    return fallback
  }

  const resolved = resolveChatErrorMessage({ code })
  return resolved === '请求失败，请稍后重试。'
    ? fallback
    : resolved
}
