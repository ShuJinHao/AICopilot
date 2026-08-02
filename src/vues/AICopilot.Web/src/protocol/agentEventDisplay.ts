import type { AgentEventPayload } from '@/types/protocols'

export function formatAgentEventDetail(event: AgentEventPayload) {
  const detail = event.detail?.trim()
  if (detail) return detail

  if (event.stage === 'agent_session_state') {
    return '会话运行状态已更新。'
  }

  return event.recoverable
    ? '运行事件已记录，可继续查看后续结果。'
    : '运行状态已更新。'
}
