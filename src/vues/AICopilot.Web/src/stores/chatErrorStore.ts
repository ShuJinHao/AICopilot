import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { ApiError } from '@/services/apiClient'
import type { ChatErrorPayload } from '@/types/protocols'

export function resolveChatErrorMessage(payload: ChatErrorPayload) {
  const userFacingMessage = payload.userFacingMessage?.trim() || payload.detail?.trim()

  switch (payload.code) {
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
    if (error.status === 401) {
      return '登录状态已失效，请重新登录。'
    }

    if (error.status === 403) {
      return '当前账号没有访问该功能的权限。'
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
