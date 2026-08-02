import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { ApiError } from '@/services/apiClient'
import type { ChatErrorPayload } from '@/types/protocols'

type ProblemLike = ChatErrorPayload & {
  title?: string
  errors?: unknown
}

export interface ChatErrorPresentation {
  message: string
  code: string | null
  detail: string | null
  userFacingMessage: string | null
}

export type ChatErrorInput = string | ChatErrorPayload | ChatErrorPresentation

function toTrimmedString(value: unknown) {
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : null
}

function collectValidationErrors(errors: unknown): string[] {
  if (!errors) {
    return []
  }

  if (Array.isArray(errors)) {
    return errors.map(toTrimmedString).filter((item): item is string => Boolean(item))
  }

  if (typeof errors !== 'object') {
    return []
  }

  const messages: string[] = []
  for (const [field, value] of Object.entries(errors as Record<string, unknown>)) {
    const fieldMessages = Array.isArray(value)
      ? value.map(toTrimmedString).filter((item): item is string => Boolean(item))
      : [toTrimmedString(value)].filter((item): item is string => Boolean(item))

    for (const message of fieldMessages) {
      messages.push(field ? `${field}: ${message}` : message)
    }
  }

  return messages
}

export function extractErrorDetail(details: unknown) {
  if (!details || typeof details !== 'object') {
    return null
  }

  const problem = details as ProblemLike
  const userFacingMessage = toTrimmedString(problem.userFacingMessage)
  if (userFacingMessage) {
    return userFacingMessage
  }

  const validationErrors = collectValidationErrors(problem.errors)
  if (validationErrors.length > 0) {
    return validationErrors.join('；')
  }

  const detail = toTrimmedString(problem.detail)
  if (detail) {
    return detail
  }

  const title = toTrimmedString(problem.title)
  if (title) {
    return title
  }

  return null
}

function extractProblemDetail(details: unknown) {
  if (!details || typeof details !== 'object') {
    return null
  }

  const problem = details as ProblemLike
  const validationErrors = collectValidationErrors(problem.errors)
  const detail = toTrimmedString(problem.detail)
  if (detail || validationErrors.length > 0) {
    return [detail, ...validationErrors].filter(Boolean).join('；')
  }

  return toTrimmedString(problem.title)
}

export function resolveChatErrorMessage(payload: ChatErrorPayload) {
  const userFacingMessage = toTrimmedString(payload.userFacingMessage)

  switch (payload.code) {
    case 'missing_permission':
      return userFacingMessage ?? '当前账号缺少执行该操作的权限。'
    case 'cloud_readonly_tool_disabled':
      return userFacingMessage ?? 'Cloud 只读工具尚未启用，请联系管理员在 Tool Registry 中开启。'
    case 'tool_requires_approval':
      return userFacingMessage ?? '该工具需要人工审批，请先处理审批队列。'
    case 'tool_disabled':
      return userFacingMessage ?? '该工具已被禁用，不能执行。'
    case 'tool_blocked':
      return userFacingMessage ?? '该工具被安全策略阻断。'
    case 'tool_permission_denied':
      return userFacingMessage ?? '当前账号没有调用该工具的权限。'
    case 'tool_output_schema_invalid':
      return (
        userFacingMessage ??
        '工具输出与注册契约不一致，本次执行未记为成功，结果不可用于后续审批或完成，请联系管理员检查工具配置。'
      )
    case 'approval_pending':
      return userFacingMessage ?? '当前会话已有待处理审批，请先处理审批请求。'
    case 'agent_session_reset_required':
      return userFacingMessage ?? '当前会话的 AgentSession 已过期或无法恢复，请新建会话后继续。'
    case 'agent_session_interrupted':
      return userFacingMessage ?? '上一次执行已中断；系统不会自动重放，请新建会话后继续。'
    case 'agent_session_version_conflict':
      return userFacingMessage ?? '会话状态已变化，请刷新后重试。'
    case 'chat_context_expired':
    case 'approval_already_processed':
      return userFacingMessage ?? '审批上下文已失效，请重新发起请求。'
    case 'rate_limit_exceeded':
      return userFacingMessage ?? '请求过于频繁，请稍后再试。'
    case 'approval_stream_failed':
      return userFacingMessage ?? '审批处理失败，请稍后重试。'
    case 'chat_stream_failed':
      return userFacingMessage ?? '对话执行失败，请稍后重试。'
    case 'chat_configuration_missing':
      return userFacingMessage ?? '当前对话配置不可用，请检查模型和模板配置。'
    case 'token_budget_exceeded':
      return userFacingMessage ?? '当前上下文过长，请新建会话后重试。'
    case 'model_provider_unavailable':
      return userFacingMessage ?? '模型服务暂时不可用，请稍后重试或联系管理员检查模型网络。'
    case 'model_request_timeout':
      return userFacingMessage ?? '模型响应超时，请稍后重试或缩小问题范围。'
    case 'client_stream_timeout':
      return userFacingMessage ?? '对话连接长时间无响应，请重试。'
    default:
      return userFacingMessage ?? '请求失败，请稍后重试。'
  }
}

function isChatErrorPresentation(value: ChatErrorInput): value is ChatErrorPresentation {
  return typeof value === 'object' && value !== null && 'message' in value
}

export function createChatErrorPresentation(input: ChatErrorInput): ChatErrorPresentation {
  if (typeof input === 'string') {
    return {
      message: toTrimmedString(input) ?? '请求失败，请稍后重试。',
      code: null,
      detail: null,
      userFacingMessage: null,
    }
  }

  if (isChatErrorPresentation(input)) {
    return input
  }

  const code = toTrimmedString(input.code)
  const detail = toTrimmedString(input.detail)
  const userFacingMessage = toTrimmedString(input.userFacingMessage)
  const message =
    userFacingMessage ??
    (!code && detail ? detail : resolveChatErrorMessage({ ...input, code: code ?? undefined }))

  return {
    message,
    code,
    detail,
    userFacingMessage,
  }
}

export function toChatErrorPresentation(error: unknown): ChatErrorPresentation {
  if (error instanceof ApiError) {
    const problem =
      error.details && typeof error.details === 'object' ? (error.details as ProblemLike) : null

    if (problem) {
      const code = toTrimmedString(problem.code)
      const detail = extractProblemDetail(problem)
      const userFacingMessage = toTrimmedString(problem.userFacingMessage)
      if (code || detail || userFacingMessage) {
        return createChatErrorPresentation({
          code: code ?? undefined,
          detail: detail ?? undefined,
          userFacingMessage,
        })
      }
    }

    if (error.status === 401) {
      return createChatErrorPresentation('登录状态已失效，请重新登录。')
    }

    if (error.status === 403) {
      return createChatErrorPresentation('当前账号没有访问该功能的权限。')
    }

    if (error.status === 429) {
      return createChatErrorPresentation('请求过于频繁，请稍后再试。')
    }
  }

  return createChatErrorPresentation('请求失败，请稍后重试。')
}

export function toFriendlyMessage(error: unknown) {
  return toChatErrorPresentation(error).message
}

export const useChatErrorStore = defineStore('chatError', () => {
  const activeError = ref<ChatErrorPresentation | null>(null)
  const errorSessionId = ref<string | null>(null)
  const currentSessionId = ref<string | null>(null)

  const errorPresentation = computed(() => {
    if (!currentSessionId.value || errorSessionId.value !== currentSessionId.value) {
      return null
    }

    return activeError.value
  })
  const errorMessage = computed(() => errorPresentation.value?.message ?? '')

  function bindCurrentSession(sessionId: string | null) {
    currentSessionId.value = sessionId
  }

  function setSessionError(sessionId: string, error: ChatErrorInput) {
    errorSessionId.value = sessionId
    activeError.value = createChatErrorPresentation(error)
  }

  function clearSessionError(sessionId: string | null = currentSessionId.value) {
    if (!sessionId || errorSessionId.value === sessionId) {
      errorSessionId.value = null
      activeError.value = null
    }
  }

  function reset() {
    activeError.value = null
    errorSessionId.value = null
    currentSessionId.value = null
  }

  return {
    errorPresentation,
    errorMessage,
    bindCurrentSession,
    setSessionError,
    clearSessionError,
    reset,
  }
})
