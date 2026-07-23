import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { ApiError } from '@/services/apiClient'
import type { ChatErrorPayload } from '@/types/protocols'

type ProblemLike = ChatErrorPayload & {
  title?: string
  errors?: unknown
}

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

export function resolveChatErrorMessage(payload: ChatErrorPayload) {
  const userFacingMessage = toTrimmedString(payload.userFacingMessage)

  switch (payload.code) {
    case 'missing_permission':
      return userFacingMessage ?? '当前账号缺少执行该操作的权限。'
    case 'cloud_readonly_tool_disabled':
      return userFacingMessage ?? 'Cloud 只读工具尚未启用，请联系管理员在 Tool Registry 中开启。'
    case 'tool_requires_approval':
      return userFacingMessage ?? '该工具需要人工审批，请先处理审批队列。'
    case 'agent_task_run_queued':
      return userFacingMessage ?? '任务已经进入队列，请勿重复运行。'
    case 'agent_task_run_in_progress':
      return userFacingMessage ?? '任务正在执行中，请等待当前运行完成。'
    case 'agent_worker_unavailable':
      return userFacingMessage ?? '当前没有可用 DataWorker，请检查 Worker 状态。'
    case 'agent_worker_workspace_mismatch':
      return userFacingMessage ?? 'Worker 与 HttpApi 工作区不一致，请先修复部署配置。'
    case 'artifact_finalized':
      return userFacingMessage ?? '产物已正式输出，不能继续编辑。'
    case 'workspace_manifest_invalid':
      return userFacingMessage ?? '工作区清单无效，请刷新后重试或联系管理员检查产物目录。'
    case 'tool_disabled':
      return userFacingMessage ?? '该工具已被禁用，不能执行。'
    case 'tool_blocked':
      return userFacingMessage ?? '该工具被安全策略阻断。'
    case 'tool_permission_denied':
      return userFacingMessage ?? '当前账号没有调用该工具的权限。'
    case 'agent_plan_invalid':
      return userFacingMessage ?? 'Agent 计划未通过后端校验，请调整任务目标后重新生成。'
    case 'planner_model_unavailable':
      return userFacingMessage ?? '计划生成模型暂时不可用，请稍后重试或联系管理员检查模型配置。'
    case 'plan_payload_too_large':
      return userFacingMessage ?? '计划内容超过固定大小上限，请缩小目标范围或减少产物后重新生成。'
    case 'evidence_payload_too_large':
      return userFacingMessage ?? '证据载荷超过固定大小上限，请缩小查询或分批生成。'
    case 'agent_plan_tool_denied':
      return userFacingMessage ?? '计划包含超出当前请求范围或权限范围的工具，请调整目标后重新生成。'
    case 'agent_plan_schema_invalid':
      return userFacingMessage ?? '计划步骤输入不符合工具 schema，请重新生成计划。'
    case 'tool_output_schema_invalid':
      return userFacingMessage ?? '工具输出与注册契约不一致，本次执行未记为成功，结果不可用于后续审批或完成，请联系管理员检查工具配置。'
    case 'planner_tool_schema_unsupported':
      return userFacingMessage ?? 'Planner 收到不支持的工具 schema，请检查工具注册信息。'
    case 'agent_task_retry_not_allowed':
      return userFacingMessage ?? '当前任务状态不允许重试。'
    case 'approval_pending':
      return userFacingMessage ?? '当前会话已有待处理审批，请先处理审批请求。'
    case 'chat_context_expired':
    case 'approval_already_processed':
      return userFacingMessage ?? '审批上下文已失效，请重新发起请求。'
    case 'rate_limit_exceeded':
      return userFacingMessage ?? '请求过于频繁，请稍后再试。'
    case 'onsite_presence_required':
      return userFacingMessage ?? '该能力需要先确认现场有人在岗。'
    case 'onsite_presence_expired':
      return userFacingMessage ?? '当前会话的在岗声明已过期，请重新确认。'
    case 'approval_reconfirmation_required':
      return userFacingMessage ?? '审批前需要再次确认现场有人在岗。'
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

export function toFriendlyMessage(error: unknown) {
  if (error instanceof ApiError) {
    const problem =
      error.details && typeof error.details === 'object'
        ? (error.details as ChatErrorPayload)
        : null

    if (problem?.code || problem?.userFacingMessage) {
      return resolveChatErrorMessage(problem)
    }

    const detail = extractErrorDetail(error.details)
    if (detail) {
      return detail
    }

    if (error.status === 401) {
      return '登录状态已失效，请重新登录。'
    }

    if (error.status === 403) {
      return '当前账号没有访问该功能的权限。'
    }

    if (error.status === 429) {
      return '请求过于频繁，请稍后再试。'
    }

    return '请求失败，请稍后重试。'
  }

  return '请求失败，请稍后重试。'
}

export const useChatErrorStore = defineStore('chatError', () => {
  const activeErrorMessage = ref('')
  const errorSessionId = ref<string | null>(null)
  const currentSessionId = ref<string | null>(null)

  const errorMessage = computed(() => {
    if (!currentSessionId.value || errorSessionId.value !== currentSessionId.value) {
      return ''
    }

    return activeErrorMessage.value
  })

  function bindCurrentSession(sessionId: string | null) {
    currentSessionId.value = sessionId
  }

  function setSessionError(sessionId: string, message: string) {
    errorSessionId.value = sessionId
    activeErrorMessage.value = message
  }

  function clearSessionError(sessionId: string | null = currentSessionId.value) {
    if (!sessionId || errorSessionId.value === sessionId) {
      errorSessionId.value = null
      activeErrorMessage.value = ''
    }
  }

  function reset() {
    activeErrorMessage.value = ''
    errorSessionId.value = null
    currentSessionId.value = null
  }

  return {
    errorMessage,
    bindCurrentSession,
    setSessionError,
    clearSessionError,
    reset,
  }
})
