import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { ApiError } from '@/services/apiClient'
import { chatService } from '@/services/chatService'
import {
  ChunkType,
  MessageRole,
  type ChatChunk,
  type ChatErrorPayload,
  type FunctionApprovalRequest,
  type IntentResult,
  type Session
} from '@/types/protocols'
import type {
  ApprovalChunk,
  ChatMessage,
  FunctionCall,
  FunctionCallChunk,
  IntentChunk,
  WidgetChunk
} from '@/types/models'

const CURRENT_SESSION_KEY = 'aicopilot.chat.currentSessionId'

function isApprovalChunk(chunk: ChatChunk): chunk is ApprovalChunk {
  return chunk.type === ChunkType.ApprovalRequest
}

export const useChatStore = defineStore('chat', () => {
  const sessions = ref<Session[]>([])
  const currentSessionId = ref<string | null>(sessionStorage.getItem(CURRENT_SESSION_KEY))
  const messagesMap = ref<Record<string, ChatMessage[]>>({})
  const isStreaming = ref(false)
  const isWaitingForApproval = ref(false)
  const isLoadingHistory = ref(false)
  const activeErrorMessage = ref('')
  const errorSessionId = ref<string | null>(null)

  const currentMessages = computed(() => {
    if (!currentSessionId.value) {
      return []
    }

    return messagesMap.value[currentSessionId.value] || []
  })

  const currentSession = computed(() => {
    if (!currentSessionId.value) {
      return null
    }

    return sessions.value.find((session) => session.id === currentSessionId.value) ?? null
  })

  const errorMessage = computed(() => {
    if (!currentSessionId.value || errorSessionId.value !== currentSessionId.value) {
      return ''
    }

    return activeErrorMessage.value
  })

  function persistCurrentSession(sessionId: string | null) {
    currentSessionId.value = sessionId

    if (sessionId) {
      sessionStorage.setItem(CURRENT_SESSION_KEY, sessionId)
      return
    }

    sessionStorage.removeItem(CURRENT_SESSION_KEY)
  }

  function upsertSession(session: Session) {
    const index = sessions.value.findIndex((item) => item.id === session.id)
    if (index >= 0) {
      sessions.value[index] = session
      return
    }

    sessions.value = [session, ...sessions.value]
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

  function syncWaitingForApproval(sessionId: string | null = currentSessionId.value) {
    if (!sessionId) {
      isWaitingForApproval.value = false
      return
    }

    if (sessionId !== currentSessionId.value) {
      return
    }

    const messages = messagesMap.value[sessionId] ?? []
    isWaitingForApproval.value = messages.some((message) =>
      message.chunks.some((chunk) => isApprovalChunk(chunk) && chunk.status === 'pending')
    )
  }

  async function loadSessions() {
    sessions.value = await chatService.getSessions()
  }

  async function refreshPendingApprovals(sessionId: string) {
    try {
      const pendingApprovals = await chatService.getPendingApprovals(sessionId)
      reconcilePendingApprovalCards(sessionId, pendingApprovals)
    } catch (error) {
      setSessionError(sessionId, toFriendlyMessage(error))
    } finally {
      syncWaitingForApproval(sessionId)
    }
  }

  async function loadHistory(sessionId: string, force = false) {
    if (!force && messagesMap.value[sessionId]?.length) {
      await refreshPendingApprovals(sessionId)
      syncWaitingForApproval(sessionId)
      return
    }

    isLoadingHistory.value = true

    try {
      const history = await chatService.getHistory(sessionId)
      messagesMap.value[sessionId] = history.map((message) => ({
        sessionId: message.sessionId,
        role: message.role === MessageRole.User ? MessageRole.User : MessageRole.Assistant,
        chunks: [
          {
            source: message.role === MessageRole.User ? 'User' : 'FinalAgentRunExecutor',
            type: ChunkType.Text,
            content: message.content
          }
        ],
        isStreaming: false,
        timestamp: new Date(message.createdAt).getTime()
      }))
      await refreshPendingApprovals(sessionId)
      syncWaitingForApproval(sessionId)
    } finally {
      isLoadingHistory.value = false
    }
  }

  async function initialize() {
    clearSessionError()
    await loadSessions()

    if (sessions.value.length === 0) {
      await createNewSession()
      return
    }

    const restoredSessionId = currentSessionId.value
    const initialSession =
      sessions.value.find((session) => session.id === restoredSessionId) ??
      sessions.value[0] ??
      null

    if (!initialSession) {
      persistCurrentSession(null)
      return
    }

    await selectSession(initialSession.id)
  }

  async function createNewSession() {
    clearSessionError()
    const newSession = await chatService.createSession()
    upsertSession(newSession)
    messagesMap.value[newSession.id] = []
    persistCurrentSession(newSession.id)
    isStreaming.value = false
    isWaitingForApproval.value = false
    return newSession
  }

  async function selectSession(id: string, forceReload = false) {
    persistCurrentSession(id)
    clearSessionError(id)
    await loadHistory(id, forceReload)
  }

  async function confirmOnsitePresence(expiresInMinutes = 30) {
    if (!currentSessionId.value) {
      return
    }

    clearSessionError(currentSessionId.value)
    const updatedSession = await chatService.updateSessionSafetyAttestation(
      currentSessionId.value,
      true,
      expiresInMinutes
    )
    upsertSession(updatedSession)
    await refreshPendingApprovals(updatedSession.id)
    return updatedSession
  }

  async function clearOnsitePresence() {
    if (!currentSessionId.value) {
      return
    }

    clearSessionError(currentSessionId.value)
    const updatedSession = await chatService.updateSessionSafetyAttestation(
      currentSessionId.value,
      false
    )
    upsertSession(updatedSession)
    await refreshPendingApprovals(updatedSession.id)
    return updatedSession
  }

  async function sendMessage(input: string) {
    if (!currentSessionId.value || isStreaming.value || isWaitingForApproval.value) {
      return
    }

    const sessionId = currentSessionId.value
    clearSessionError(sessionId)

    const userMessage = addMessage(sessionId, {
      sessionId,
      role: MessageRole.User,
      chunks: [
        {
          source: 'User',
          type: ChunkType.Text,
          content: input
        }
      ],
      isStreaming: false,
      timestamp: Date.now()
    })

    const assistantMessage = addMessage(sessionId, {
      sessionId,
      role: MessageRole.Assistant,
      chunks: [],
      isStreaming: true,
      timestamp: Date.now()
    })

    isStreaming.value = true
    let streamErrorCode: string | null = null

    try {
      await chatService.sendMessageStream(sessionId, input, {
        onChunkReceived(chunk) {
          if (chunk.type === ChunkType.Error) {
            streamErrorCode = getErrorCode(chunk)
          }

          processChunk(assistantMessage, chunk)
        },
        onComplete() {
          isStreaming.value = false
          assistantMessage.isStreaming = false
          if (streamErrorCode === 'approval_pending') {
            removeMessages(sessionId, userMessage, assistantMessage)
            void refreshPendingApprovals(sessionId)
          }

          syncWaitingForApproval(sessionId)
        },
        onError(error) {
          isStreaming.value = false
          assistantMessage.isStreaming = false
          setSessionError(sessionId, toFriendlyMessage(error))
          syncWaitingForApproval(sessionId)
        }
      })
    } catch (error) {
      setSessionError(sessionId, toFriendlyMessage(error))
    } finally {
      isStreaming.value = false
      assistantMessage.isStreaming = false
      syncWaitingForApproval(sessionId)
    }
  }

  async function submitApproval(
    callId: string,
    decision: 'approved' | 'rejected',
    onsiteConfirmed: boolean,
    chunk: ApprovalChunk
  ) {
    if (!currentSessionId.value || isStreaming.value) {
      return false
    }

    const sessionId = currentSessionId.value
    let approvalFailed = false
    let approvalErrorCode: string | null = null
    clearSessionError(sessionId)
    isStreaming.value = true

    let targetMessage = getLastAssistantMessage(sessionId)
    if (!targetMessage) {
      targetMessage = addMessage(sessionId, {
        sessionId,
        role: MessageRole.Assistant,
        chunks: [],
        isStreaming: true,
        timestamp: Date.now()
      })
    } else {
      targetMessage.isStreaming = true
    }

    try {
      await chatService.sendApprovalDecisionStream(sessionId, callId, decision, onsiteConfirmed, chunk.request, {
        onChunkReceived(incomingChunk) {
          if (incomingChunk.type === ChunkType.Error) {
            approvalFailed = true
            approvalErrorCode = getErrorCode(incomingChunk)
          }

          processChunk(targetMessage!, incomingChunk)
        },
        onComplete() {
          isStreaming.value = false
          if (targetMessage) {
            targetMessage.isStreaming = false
          }

          chunk.status = approvalFailed ? getApprovalFailureStatus(approvalErrorCode) : decision
          syncWaitingForApproval(sessionId)
        },
        onError(error) {
          approvalFailed = true
          isStreaming.value = false
          if (targetMessage) {
            targetMessage.isStreaming = false
          }

          chunk.status = 'pending'
          setSessionError(sessionId, toFriendlyMessage(error))
          syncWaitingForApproval(sessionId)
        }
      })
    } catch (error) {
      approvalFailed = true
      chunk.status = 'pending'
      setSessionError(sessionId, toFriendlyMessage(error))
    } finally {
      isStreaming.value = false
      if (targetMessage) {
        targetMessage.isStreaming = false
      }

      if (approvalFailed) {
        await refreshPendingApprovals(sessionId)
      }

      syncWaitingForApproval(sessionId)
    }

    return !approvalFailed
  }

  function reset() {
    sessions.value = []
    persistCurrentSession(null)
    messagesMap.value = {}
    isStreaming.value = false
    isWaitingForApproval.value = false
    isLoadingHistory.value = false
    activeErrorMessage.value = ''
    errorSessionId.value = null
  }

  function addMessage(sessionId: string, message: ChatMessage) {
    if (!messagesMap.value[sessionId]) {
      messagesMap.value[sessionId] = []
    }

    const list = messagesMap.value[sessionId]!
    list.push(message)
    return list[list.length - 1]!
  }

  function removeMessages(sessionId: string, ...messages: ChatMessage[]) {
    const list = messagesMap.value[sessionId]
    if (!list?.length) {
      return
    }

    const targets = new Set(messages)
    messagesMap.value[sessionId] = list.filter((message) => !targets.has(message))
  }

  function getApprovalChunks(sessionId: string) {
    return (messagesMap.value[sessionId] ?? [])
      .flatMap((message) => message.chunks)
      .filter(isApprovalChunk)
  }

  function reconcilePendingApprovalCards(
    sessionId: string,
    pendingApprovals: FunctionApprovalRequest[]
  ) {
    const pendingIds = new Set(pendingApprovals.map((approval) => approval.callId))
    const existingChunks = getApprovalChunks(sessionId)

    for (const chunk of existingChunks) {
      if (chunk.status === 'pending' && !pendingIds.has(chunk.request.callId)) {
        chunk.status = 'expired'
      }
    }

    for (const approval of pendingApprovals) {
      const chunk = existingChunks.find((item) => item.request.callId === approval.callId)
      if (chunk) {
        chunk.request = approval
        if (chunk.status === 'expired') {
          chunk.status = 'pending'
        }
      }
    }

    const existingIds = new Set(existingChunks.map((chunk) => chunk.request.callId))
    const missingApprovals = pendingApprovals.filter((approval) => !existingIds.has(approval.callId))
    if (missingApprovals.length === 0) {
      return
    }

    addMessage(sessionId, {
      sessionId,
      role: MessageRole.Assistant,
      chunks: missingApprovals.map((approval) => ({
        source: 'FinalAgentRunExecutor',
        type: ChunkType.ApprovalRequest,
        content: JSON.stringify(approval),
        request: approval,
        status: 'pending'
      }) as ApprovalChunk),
      isStreaming: false,
      timestamp: Date.now()
    })
  }

  function addTextChunk(message: ChatMessage, chunk: ChatChunk) {
    const previousChunk = message.chunks[message.chunks.length - 1]

    if (!previousChunk) {
      message.chunks.push(chunk)
      return
    }

    if (previousChunk.source === chunk.source && previousChunk.type === ChunkType.Text) {
      previousChunk.content += chunk.content
      return
    }

    message.chunks.push(chunk)
  }

  function addWidgetChunk(message: ChatMessage, chunk: ChatChunk, parsedWidget: unknown) {
    message.chunks.push({
      ...chunk,
      type: ChunkType.Widget,
      widget: parsedWidget
    } as WidgetChunk)
  }

  function addIntentChunk(message: ChatMessage, chunk: ChatChunk) {
    try {
      const intents = JSON.parse(chunk.content) as IntentResult[]
      message.chunks.push({ ...chunk, intents } as IntentChunk)
    } catch {
      addTextChunk(message, chunk)
    }
  }

  function addFunctionCallChunk(message: ChatMessage, chunk: ChatChunk) {
    try {
      const functionCall = JSON.parse(chunk.content) as FunctionCall
      functionCall.status = 'calling'
      message.chunks.push({ ...chunk, functionCall } as FunctionCallChunk)
    } catch {
      addTextChunk(message, chunk)
    }
  }

  function addFunctionResultChunk(message: ChatMessage, chunk: ChatChunk) {
    try {
      const functionResult = JSON.parse(chunk.content) as FunctionCall
      const functionCallChunks = message.chunks.filter(
        (item) => item.type === ChunkType.FunctionCall
      ) as FunctionCallChunk[]
      const functionCallChunk = functionCallChunks.find((item) => item.functionCall.id === functionResult.id)

      if (functionCallChunk) {
        functionCallChunk.functionCall.result = functionResult.result
        functionCallChunk.functionCall.status = 'completed'
      }
    } catch {
      // ignore malformed tool result payloads
    }
  }

  function addApprovalRequestChunk(message: ChatMessage, chunk: ChatChunk) {
    try {
      const request = JSON.parse(chunk.content) as FunctionApprovalRequest
      message.chunks.push({
        ...chunk,
        request,
        status: 'pending'
      } as ApprovalChunk)
      syncWaitingForApproval(message.sessionId)
    } catch {
      setSessionError(message.sessionId, '审批请求解析失败。')
    }
  }

  function addErrorChunk(message: ChatMessage, chunk: ChatChunk) {
    try {
      const payload = JSON.parse(chunk.content) as ChatErrorPayload
      const userFacingMessage = payload.userFacingMessage?.trim() || payload.detail?.trim()

      if (userFacingMessage) {
        addTextChunk(message, {
          ...chunk,
          type: ChunkType.Text,
          content: userFacingMessage
        })
      }

      switch (payload.code) {
        case 'approval_pending':
          setSessionError(message.sessionId, userFacingMessage ?? '当前会话已有待处理审批，请先处理审批请求。')
          break
        case 'chat_context_expired':
        case 'approval_already_processed':
          setSessionError(message.sessionId, userFacingMessage ?? '审批上下文已失效，请重新发起请求。')
          break
        case 'rate_limit_exceeded':
          setSessionError(message.sessionId, userFacingMessage ?? '请求过于频繁，请稍后再试。')
          break
        case 'onsite_presence_required':
          setSessionError(message.sessionId, userFacingMessage ?? '该能力需要先确认现场有人在岗。')
          break
        case 'onsite_presence_expired':
          setSessionError(message.sessionId, userFacingMessage ?? '当前会话的在岗声明已过期，请重新确认。')
          break
        case 'approval_reconfirmation_required':
          setSessionError(message.sessionId, userFacingMessage ?? '审批前需要再次确认现场有人在岗。')
          break
        case 'approval_stream_failed':
          setSessionError(message.sessionId, userFacingMessage ?? '审批处理失败，请稍后重试。')
          break
        case 'chat_stream_failed':
          setSessionError(message.sessionId, userFacingMessage ?? '对话执行失败，请稍后重试。')
          break
        case 'chat_configuration_missing':
          setSessionError(message.sessionId, userFacingMessage ?? '当前对话配置不可用，请检查模型和模板配置。')
          break
        case 'token_budget_exceeded':
          setSessionError(message.sessionId, userFacingMessage ?? '当前上下文过长，请新建会话后重试。')
          break
        case 'control_action_blocked':
        case 'capability_not_allowed':
          if (userFacingMessage) {
            setSessionError(message.sessionId, userFacingMessage)
          }
          break
        default:
          setSessionError(message.sessionId, userFacingMessage ?? '请求失败，请稍后重试。')
          break
      }
    } catch {
      setSessionError(message.sessionId, '请求失败，请稍后重试。')
    }
  }

  function getLastAssistantMessage(sessionId: string) {
    const list = messagesMap.value[sessionId]
    if (!list?.length) {
      return null
    }

    const lastMessage = list[list.length - 1]!
    return lastMessage.role === MessageRole.Assistant ? lastMessage : null
  }

  function processChunk(message: ChatMessage, chunk: ChatChunk) {
    if (chunk.type === ChunkType.Text) {
      const content = chunk.content.trim()
      if (content.includes('"visual_decision"') || content.includes('"VisualDecision"')) {
        try {
          const jsonMatch = content.match(/(\{[\s\S]*"visual_decision"[\s\S]*?\})/)
          if (jsonMatch) {
            const payload = JSON.parse(jsonMatch[0]) as Record<string, unknown>
            const decision = payload.visual_decision || payload.VisualDecision
            if (decision) {
              let finalData: unknown[] = []

              if (Array.isArray(payload.data) && payload.data.length > 0) {
                finalData = payload.data
              } else if (
                typeof decision === 'object' &&
                decision !== null &&
                Array.isArray((decision as { data?: unknown[] }).data) &&
                ((decision as { data?: unknown[] }).data?.length ?? 0) > 0
              ) {
                finalData = (decision as { data: unknown[] }).data
              } else {
                for (let index = message.chunks.length - 1; index >= 0; index -= 1) {
                  const existingChunk = message.chunks[index]
                  if (!existingChunk || existingChunk.type !== ChunkType.FunctionCall) {
                    continue
                  }

                  const functionCall = (existingChunk as FunctionCallChunk).functionCall
                  if (functionCall.status !== 'completed' || !functionCall.result) {
                    continue
                  }

                  try {
                    const parsed = JSON.parse(functionCall.result)
                    if (Array.isArray(parsed) && parsed.length > 0) {
                      finalData = parsed
                      break
                    }
                  } catch {
                    // ignore invalid JSON payloads
                  }
                }
              }

              addWidgetChunk(message, chunk, { ...payload, data: finalData })

              const remainingText = content.replace(jsonMatch[0], '').trim()
              if (remainingText) {
                addTextChunk(message, { ...chunk, content: remainingText })
              }
              return
            }
          }
        } catch {
          // fall back to plain text rendering
        }
      }
    }

    switch (chunk.type) {
      case ChunkType.Text:
        addTextChunk(message, chunk)
        break
      case ChunkType.Intent:
        addIntentChunk(message, chunk)
        break
      case ChunkType.FunctionCall:
        addFunctionCallChunk(message, chunk)
        break
      case ChunkType.FunctionResult:
        addFunctionResultChunk(message, chunk)
        break
      case ChunkType.Widget:
        try {
          addWidgetChunk(message, chunk, JSON.parse(chunk.content))
        } catch {
          addTextChunk(message, chunk)
        }
        break
      case ChunkType.ApprovalRequest:
        addApprovalRequestChunk(message, chunk)
        break
      case ChunkType.Error:
        addErrorChunk(message, chunk)
        break
    }
  }

  function getErrorCode(chunk: ChatChunk) {
    try {
      const payload = JSON.parse(chunk.content) as ChatErrorPayload
      return payload.code ?? null
    } catch {
      return null
    }
  }

  function getApprovalFailureStatus(errorCode: string | null): ApprovalChunk['status'] {
    return errorCode === 'approval_already_processed' || errorCode === 'chat_context_expired'
      ? 'expired'
      : 'pending'
  }

  function toFriendlyMessage(error: unknown) {
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

  return {
    sessions,
    currentSessionId,
    currentSession,
    currentMessages,
    isStreaming,
    isWaitingForApproval,
    isLoadingHistory,
    errorMessage,
    initialize,
    createNewSession,
    selectSession,
    confirmOnsitePresence,
    clearOnsitePresence,
    sendMessage,
    submitApproval,
    reset
  }
})
