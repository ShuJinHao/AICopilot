export type ChatAgentMode = 'plan' | 'execute'

export interface ChatModePresentation {
  mode: ChatAgentMode
  label: string
  shortLabel: string
  title: string
  description: string
  placeholder: string
  suggestions: readonly string[]
}

export type ConversationStatusKey =
  | 'initializing'
  | 'ready'
  | 'running'
  | 'waiting-approval'
  | 'completed'
  | 'failed'
  | 'interrupted'
  | 'reset-required'

export interface ConversationStatusPresentation {
  key: ConversationStatusKey
  label: string
  tone: 'neutral' | 'blue' | 'warning' | 'danger' | 'success'
}

export interface ConversationStatusContext {
  isSessionActivating: boolean
  agentSessionStatus?: string | null
  agentSessionResetRequired?: boolean
  isStreaming: boolean
  hasPendingApproval: boolean
  hasMessages: boolean
  hasError: boolean
}

const modePresentations: Record<ChatAgentMode, ChatModePresentation> = {
  plan: {
    mode: 'plan',
    label: 'Plan · 规划',
    shortLabel: '规划模式',
    title: '先整理思路和待办',
    description: '只规划和整理待办，不查询外部数据。',
    placeholder: '描述目标，助手会整理分析步骤和待办，不会查询外部数据',
    suggestions: [
      '为 DEV-001 设备日志异常排查整理分析步骤，不执行查询',
      '规划一次设备运行状态核查，列出需要确认的信息',
      '把工序主数据核对目标拆成一份待办清单',
      '为客户端版本核对制定检查步骤和完成标准',
    ],
  },
  execute: {
    mode: 'execute',
    label: 'Execute · 执行',
    shortLabel: '执行模式',
    title: '直接提问或查询只读数据',
    description: '可直接回答，并按权限调用只读工具。',
    placeholder: '输入问题，助手可直接回答并按权限使用只读工具',
    suggestions: [
      '查看 DEV-001 最近 24 小时设备日志，并给出根因线索',
      '查看 DEV-001 最后上报运行状态和心跳时间',
      '列出工序主数据，并说明正式字段边界',
      '列出 stable 通道、win-x64 运行时的已发布客户端版本',
    ],
  },
}

export function getChatModePresentation(
  mode: ChatAgentMode | null | undefined,
): ChatModePresentation {
  return modePresentations[mode === 'execute' ? 'execute' : 'plan']
}

export function resolveConversationStatus(
  context: ConversationStatusContext,
): ConversationStatusPresentation {
  if (context.isSessionActivating) {
    return { key: 'initializing', label: '初始化中', tone: 'warning' }
  }

  if (context.agentSessionResetRequired || context.agentSessionStatus === 'ResetRequired') {
    return { key: 'reset-required', label: '需新建会话', tone: 'danger' }
  }

  if (context.agentSessionStatus === 'Interrupted') {
    return { key: 'interrupted', label: '已中断', tone: 'danger' }
  }

  if (context.isStreaming || context.agentSessionStatus === 'Running') {
    return { key: 'running', label: '运行中', tone: 'blue' }
  }

  if (context.hasError) {
    return { key: 'failed', label: '失败', tone: 'danger' }
  }

  if (context.hasPendingApproval) {
    return { key: 'waiting-approval', label: '等待批准', tone: 'warning' }
  }

  if (context.hasMessages) {
    return { key: 'completed', label: '已完成', tone: 'success' }
  }

  return { key: 'ready', label: '就绪', tone: 'success' }
}

export function requiresNewConversation(status: string | null | undefined, resetRequired = false) {
  return resetRequired || status === 'Interrupted' || status === 'ResetRequired'
}
