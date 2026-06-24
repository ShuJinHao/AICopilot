import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { ApiError } from '@/services/apiClient'
import type { ChatErrorPayload } from '@/types/protocols'

export function resolveChatErrorMessage(payload: ChatErrorPayload) {
  const userFacingMessage = payload.userFacingMessage?.trim() || payload.detail?.trim()

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
    case 'planner_model_unavailable':
      return userFacingMessage ?? '规划模型不可用，请检查模型配置。'
    case 'tool_disabled':
      return userFacingMessage ?? '该工具已被禁用，不能执行。'
    case 'tool_blocked':
      return userFacingMessage ?? '该工具被安全策略阻断。'
    case 'tool_permission_denied':
      return userFacingMessage ?? '当前账号没有调用该工具的权限。'
    case 'agent_plan_invalid':
      return userFacingMessage ?? 'Agent 计划未通过后端校验，请调整任务目标后重新生成。'
    case 'agent_plan_tool_denied':
      return userFacingMessage ?? '计划包含当前 Skill 不允许的工具，请调整 Skill 或重新生成计划。'
    case 'agent_plan_schema_invalid':
      return userFacingMessage ?? '计划步骤输入不符合工具 schema，请重新生成计划。'
    case 'planner_tool_catalog_empty':
      return userFacingMessage ?? '当前 Skill 没有可用工具，请检查 Skill 与 Tool Registry 配置。'
    case 'planner_tool_schema_unsupported':
      return userFacingMessage ?? 'Planner 收到不支持的工具 schema，请检查工具注册信息。'
    case 'agent_skill_selection_required':
      return userFacingMessage ?? '无法自动识别合适的 Skill，请补充任务目标或手动选择 Skill 后重试。'
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
    default:
      return userFacingMessage ?? '请求失败，请稍后重试。'
  }
}

export function toFriendlyMessage(error: unknown) {
  if (error instanceof ApiError) {
    const problem = error.details && typeof error.details === 'object'
      ? error.details as ChatErrorPayload
      : null

    if (problem?.code || problem?.detail || problem?.userFacingMessage) {
      return resolveChatErrorMessage(problem)
    }

    if (error.status === 401) {
      return '登录状态已失效，请重新登录。'
    }

    if (error.status === 403) {
      return '当前账号没有访问该功能的权限。'
    }

    if (error.status === 400) {
      return '请求没有通过后端校验，请调整目标或检查模型配置后重试。'
    }

    if (error.status === 429) {
      return '请求过于频繁，请稍后再试。'
    }

    if (typeof error.message === 'string' && error.message.trim().length > 0) {
      return error.message
    }
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
    reset
  }
})
