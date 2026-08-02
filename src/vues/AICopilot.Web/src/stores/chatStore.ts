import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { chatService } from '@/services/chatService'
import { ChunkType, MessageRole } from '@/types/protocols'
import type { ApprovalChunk, ChatMessage } from '@/types/models'
import { getErrorCode, processChunk } from '@/protocol/chunkReducer'
import { getApprovalFailureStatus } from '@/protocol/approvalProtocol'
import { useAgentCatalogStore } from './agentCatalogStore'
import { useApprovalStore } from './approvalStore'
import { toFriendlyMessage, useChatErrorStore } from './chatErrorStore'
import { getChatRunMessageKey, useChatRunStatusStore } from './chatRunStatusStore'
import { useMessageStore } from './messageStore'
import { useSessionStore } from './sessionStore'
import { useStreamStore } from './streamStore'

interface HistoryCursorState {
  beforeSequence: number | null
  afterSequence: number | null
  hasMoreBefore: boolean
  hasMoreAfter: boolean
}

export const useChatStore = defineStore('chat', () => {
  const sessionStore = useSessionStore()
  const messageStore = useMessageStore()
  const streamStore = useStreamStore()
  const approvalStore = useApprovalStore()
  const catalogStore = useAgentCatalogStore()
  const errorStore = useChatErrorStore()
  const runStatusStore = useChatRunStatusStore()

  const historyCursors = ref<Record<string, HistoryCursorState>>({})
  const isLoadingOlderHistory = ref(false)
  const sessionOperationCount = ref(0)
  const initializationError = ref('')

  const sessions = computed(() => sessionStore.sessions)
  const currentSessionId = computed(() => sessionStore.currentSessionId)
  const currentSession = computed(() => sessionStore.currentSession)
  const agentMode = computed(() => currentSession.value?.agentMode ?? null)
  const agentSessionStatus = computed(() => currentSession.value?.agentSessionStatus ?? null)
  const isAgentSessionUnavailable = computed(
    () =>
      Boolean(currentSession.value?.agentSessionResetRequired) ||
      agentSessionStatus.value === 'Interrupted' ||
      agentSessionStatus.value === 'ResetRequired',
  )
  const agentSessionNotice = computed(() => {
    if (
      currentSession.value?.agentSessionResetRequired ||
      agentSessionStatus.value === 'ResetRequired'
    ) {
      return '此会话没有可恢复的 AgentSession 状态，请新建会话后继续。'
    }
    if (agentSessionStatus.value === 'Interrupted') {
      return '上一次执行已中断；系统不会自动重放模型或工具调用，请新建会话后继续。'
    }
    if (agentSessionStatus.value === 'Running' && !streamStore.isStreaming) {
      return '检测到未完成的运行状态；下一次请求会先执行中断保护，不会自动重放。'
    }
    return ''
  })
  const composerSessionId = computed(() => sessionStore.activeSession?.id ?? null)
  const resolvedSessionId = computed(() =>
    sessionStore.isSessionActivating ? null : composerSessionId.value,
  )
  const isSessionActivating = computed(() => sessionStore.isSessionActivating)
  const currentMessages = computed(() => messageStore.currentMessages)
  const isStreaming = computed(() => streamStore.isStreaming)
  const isWaitingForApproval = computed(() => approvalStore.isWaitingForApproval)
  const hasPendingApproval = computed(() => approvalStore.hasPendingApproval)
  const isApprovalAuthorityUnknown = computed(() => approvalStore.isApprovalAuthorityUnknown)
  const isLoadingHistory = computed(() => sessionStore.isLoadingHistory)
  const isSessionOperationInFlight = computed(
    () => sessionOperationCount.value > 0 || isLoadingOlderHistory.value,
  )
  const isSessionTransitionBlocked = computed(
    () =>
      sessionStore.isSessionActivating ||
      streamStore.isStreaming ||
      isSessionOperationInFlight.value,
  )
  const canEditComposerContext = computed(
    () => !isAgentSessionUnavailable.value && !sessionStore.isSessionActivating,
  )
  const canChangeAgentMode = computed(() =>
    Boolean(
      resolvedSessionId.value &&
      typeof currentSession.value?.agentSessionVersion === 'number' &&
      agentSessionStatus.value === 'Ready' &&
      !currentSession.value?.hasPendingApproval &&
      !approvalStore.isWaitingForApproval &&
      !isSessionTransitionBlocked.value,
    ),
  )
  const errorMessage = computed(() => errorStore.errorMessage || initializationError.value)
  const hasMoreHistoryBefore = computed(() => {
    const sessionId = sessionStore.currentSessionId
    return Boolean(sessionId && historyCursors.value[sessionId]?.hasMoreBefore)
  })
  const availableKnowledgeBases = computed(() => catalogStore.availableKnowledgeBases)
  const selectedKnowledgeBaseId = computed(() => catalogStore.selectedKnowledgeBaseId)
  const selectedKnowledgeBase = computed(() => catalogStore.selectedKnowledgeBase)
  const currentRunStatus = computed(() => runStatusStore.currentRunStatus)

  function bindErrorSession() {
    errorStore.bindCurrentSession(sessionStore.currentSessionId)
  }

  function clearCurrentSessionError() {
    initializationError.value = ''
    bindErrorSession()
    errorStore.clearSessionError(sessionStore.currentSessionId)
  }

  function setSessionError(sessionId: string, message: string) {
    errorStore.setSessionError(sessionId, message)
    bindErrorSession()
  }

  function applyAgentSessionEvent(event: {
    sessionId?: string
    mode?: 'plan' | 'execute'
    status?: string
    version?: number
    pendingApproval?: boolean
  }) {
    if (
      !event.sessionId ||
      (event.mode !== 'plan' && event.mode !== 'execute') ||
      typeof event.status !== 'string' ||
      typeof event.version !== 'number' ||
      typeof event.pendingApproval !== 'boolean'
    ) {
      return
    }

    sessionStore.applyAgentSessionState(event.sessionId, {
      mode: event.mode,
      status: event.status,
      version: event.version,
      pendingApproval: event.pendingApproval,
    })
  }

  function canReadPendingApprovals(sessionId: string) {
    const session = sessionStore.sessions.find((item) => item.id === sessionId)
    return Boolean(
      session &&
      !session.agentSessionResetRequired &&
      session.agentSessionStatus !== 'Interrupted' &&
      session.agentSessionStatus !== 'ResetRequired',
    )
  }

  function ownsLoadedSession(sessionId: string) {
    return sessionStore.sessions.some((session) => session.id === sessionId)
  }

  function ownsCurrentApproval(callId: string, chunk: ApprovalChunk, sessionId: string) {
    return (
      callId === chunk.request.callId &&
      chunk.status === 'pending' &&
      messageStore
        .getApprovalChunks(sessionId)
        .some((candidate) => candidate === chunk && candidate.request.callId === callId)
    )
  }

  async function runSessionOperation<T>(operation: () => Promise<T>): Promise<T> {
    sessionOperationCount.value += 1
    try {
      return await operation()
    } finally {
      sessionOperationCount.value = Math.max(0, sessionOperationCount.value - 1)
    }
  }

  function getRunStatusForMessage(message: ChatMessage) {
    return runStatusStore.getStatus(message.sessionId, getChatRunMessageKey(message))
  }

  function updateHistoryCursor(
    sessionId: string,
    page: {
      beforeSequence?: number | null
      afterSequence?: number | null
      hasMoreBefore?: boolean
      hasMoreAfter?: boolean
    },
  ) {
    historyCursors.value[sessionId] = {
      beforeSequence: page.beforeSequence ?? null,
      afterSequence: page.afterSequence ?? null,
      hasMoreBefore: Boolean(page.hasMoreBefore),
      hasMoreAfter: Boolean(page.hasMoreAfter),
    }
  }

  async function refreshApprovalProjection(sessionId: string) {
    if (canReadPendingApprovals(sessionId)) {
      await approvalStore.refreshPendingApprovals(sessionId)
    } else {
      approvalStore.reconcilePendingApprovalCards(sessionId, [])
    }
  }

  async function loadHistory(sessionId: string, force = false) {
    if (!force && messageStore.messagesMap[sessionId]?.length) {
      await refreshApprovalProjection(sessionId)
      return
    }

    sessionStore.isLoadingHistory = true
    try {
      const history = await chatService.getHistory(sessionId)
      messageStore.setHistory(sessionId, history.items)
      updateHistoryCursor(sessionId, history)
      await refreshApprovalProjection(sessionId)
    } finally {
      sessionStore.isLoadingHistory = false
    }
  }

  async function loadOlderHistory(sessionId = resolvedSessionId.value) {
    if (!sessionId || isSessionTransitionBlocked.value) return false

    const cursor = historyCursors.value[sessionId]
    if (!cursor?.hasMoreBefore || !cursor.beforeSequence) return false

    isLoadingOlderHistory.value = true
    try {
      const history = await chatService.getHistory(sessionId, {
        beforeSequence: cursor.beforeSequence,
      })
      messageStore.prependHistory(sessionId, history.items)
      updateHistoryCursor(sessionId, history)
      return history.items.length > 0
    } catch (error) {
      setSessionError(sessionId, toFriendlyMessage(error))
      return false
    } finally {
      isLoadingOlderHistory.value = false
    }
  }

  async function loadKnowledgeBases() {
    await catalogStore.loadKnowledgeBases((message) => {
      const sessionId = sessionStore.currentSessionId
      if (sessionId) setSessionError(sessionId, message)
      else initializationError.value = message
    })
  }

  function selectKnowledgeBase(knowledgeBaseId: string | null) {
    if (canEditComposerContext.value) catalogStore.selectKnowledgeBase(knowledgeBaseId)
  }

  function prepareInitialization() {
    if (sessionStore.isSessionActivating) return
    initializationError.value = ''
    sessionStore.beginSessionActivation()
    streamStore.stop()
    errorStore.clearSessionError()
  }

  async function activateSession(id: string, forceReload = false) {
    const previousCurrentSessionId = sessionStore.currentSessionId
    const previousActiveSessionId = sessionStore.activeSessionId
    sessionStore.beginSessionActivation(id)
    streamStore.stop()
    try {
      sessionStore.persistCurrentSession(id)
      bindErrorSession()
      errorStore.clearSessionError(id)
      await sessionStore.refreshSession(id)
      approvalStore.sync(id)
      await loadHistory(id, forceReload)
      sessionStore.completeSessionActivation(id)
    } catch (error) {
      sessionStore.persistCurrentSession(previousCurrentSessionId)
      sessionStore.failSessionActivation(previousActiveSessionId)
      approvalStore.sync(previousActiveSessionId)
      bindErrorSession()
      setSessionError(previousCurrentSessionId ?? id, toFriendlyMessage(error))
      throw error
    }
  }

  async function createSession() {
    const previousCurrentSessionId = sessionStore.currentSessionId
    const previousActiveSessionId = sessionStore.activeSessionId
    sessionStore.beginSessionActivation(null)
    try {
      const session = await sessionStore.createSession()
      messageStore.messagesMap[session.id] = []
      approvalStore.sync(session.id)
      bindErrorSession()
      sessionStore.completeSessionActivation(session.id)
      return session
    } catch (error) {
      sessionStore.persistCurrentSession(previousCurrentSessionId)
      sessionStore.failSessionActivation(previousActiveSessionId)
      approvalStore.sync(previousActiveSessionId)
      bindErrorSession()
      throw error
    }
  }

  async function initialize() {
    const previousCurrentSessionId = sessionStore.currentSessionId
    const previousActiveSessionId = sessionStore.activeSessionId
    if (!sessionStore.isSessionActivating) prepareInitialization()

    try {
      await sessionStore.loadSessions()
      void loadKnowledgeBases()
      if (sessionStore.sessions.length === 0) {
        await createSession()
        return
      }

      const initialSession =
        sessionStore.sessions.find((session) => session.id === previousCurrentSessionId) ??
        sessionStore.sessions[0]
      if (!initialSession) {
        sessionStore.persistCurrentSession(null)
        sessionStore.completeSessionActivation(null)
        bindErrorSession()
        return
      }

      await activateSession(initialSession.id)
    } catch (error) {
      sessionStore.persistCurrentSession(previousCurrentSessionId)
      sessionStore.failSessionActivation(previousActiveSessionId)
      approvalStore.sync(previousActiveSessionId)
      bindErrorSession()
      const message = toFriendlyMessage(error)
      if (previousCurrentSessionId) setSessionError(previousCurrentSessionId, message)
      else initializationError.value = message
      throw error
    }
  }

  async function createNewSession() {
    if (isSessionTransitionBlocked.value) return null
    clearCurrentSessionError()
    try {
      return await createSession()
    } catch (error) {
      initializationError.value = toFriendlyMessage(error)
      console.error('Failed to create a new session.', error)
      return null
    }
  }

  async function selectSession(id: string, forceReload = false) {
    if (isSessionTransitionBlocked.value) return false
    if (!ownsLoadedSession(id)) {
      const currentId = sessionStore.currentSessionId
      if (currentId) setSessionError(currentId, '会话不在当前已加载列表中，已阻止操作。')
      return false
    }

    try {
      await activateSession(id, forceReload)
      return true
    } catch (error) {
      console.error('Failed to select the requested session.', error)
      return false
    }
  }

  async function deleteSession(id: string) {
    if (isSessionTransitionBlocked.value || !ownsLoadedSession(id)) return false

    const wasCurrent = sessionStore.currentSessionId === id
    const previousActiveSessionId = sessionStore.activeSessionId
    sessionStore.beginSessionActivation(previousActiveSessionId)
    try {
      await sessionStore.deleteSession(id)
      delete messageStore.messagesMap[id]
      delete historyCursors.value[id]
      runStatusStore.clearSession(id)

      if (wasCurrent) {
        const nextSessionId = sessionStore.currentSessionId
        if (nextSessionId) await activateSession(nextSessionId, true)
        else await createSession()
      } else {
        sessionStore.completeSessionActivation(previousActiveSessionId)
      }
      return true
    } catch (error) {
      sessionStore.failSessionActivation(previousActiveSessionId)
      bindErrorSession()
      const sessionId = sessionStore.currentSessionId
      if (sessionId) setSessionError(sessionId, toFriendlyMessage(error))
      return false
    }
  }

  async function changeAgentMode(mode: 'plan' | 'execute') {
    const sessionId = resolvedSessionId.value
    const expectedVersion = currentSession.value?.agentSessionVersion
    if (!sessionId || typeof expectedVersion !== 'number' || !canChangeAgentMode.value) {
      return false
    }
    if (agentMode.value === mode) return true

    try {
      return await runSessionOperation(async () => {
        clearCurrentSessionError()
        const updated = await chatService.updateAgentMode(sessionId, mode, expectedVersion)
        sessionStore.applyAgentSessionState(sessionId, {
          mode: updated.mode,
          version: updated.version,
          status: 'Ready',
          pendingApproval: false,
        })
        return true
      })
    } catch (error) {
      setSessionError(sessionId, toFriendlyMessage(error))
      try {
        await sessionStore.refreshSession(sessionId)
      } catch (refreshError) {
        console.error('Failed to reconcile AgentSession mode after mutation failure.', refreshError)
      }
      return false
    }
  }

  function createAssistantMessage(sessionId: string) {
    return messageStore.addMessage(sessionId, {
      sessionId,
      role: MessageRole.Assistant,
      finalModelId: null,
      finalModelName: '未知',
      contextWindowTokens: null,
      maxOutputTokens: null,
      chunks: [],
      isStreaming: true,
      timestamp: Date.now(),
    })
  }

  async function sendMessage(input: string) {
    const sessionId = resolvedSessionId.value
    if (
      !sessionId ||
      !input.trim() ||
      isAgentSessionUnavailable.value ||
      isSessionTransitionBlocked.value ||
      approvalStore.isWaitingForApproval
    ) {
      return false
    }

    return await runSessionOperation(async () => {
      clearCurrentSessionError()
      const userMessage = messageStore.addMessage(sessionId, {
        sessionId,
        role: MessageRole.User,
        chunks: [{ source: 'User', type: ChunkType.Text, content: input }],
        isStreaming: false,
        timestamp: Date.now(),
      })
      const assistantMessage = createAssistantMessage(sessionId)
      const messageKey = getChatRunMessageKey(assistantMessage)
      runStatusStore.startRun(sessionId, messageKey)
      streamStore.start()
      let streamErrorCode: string | null = null
      let shouldRefreshPendingApprovals = false

      try {
        await chatService.sendMessageStream(sessionId, input, {
          onChunkReceived(chunk) {
            runStatusStore.advanceFromChunk(sessionId, messageKey, chunk)
            if (chunk.type === ChunkType.Error) streamErrorCode = getErrorCode(chunk)
            processChunk(assistantMessage, chunk, {
              setSessionError,
              onApprovalChunk: approvalStore.sync,
              onAgentSessionState: applyAgentSessionEvent,
            })
          },
          onComplete() {
            streamStore.stop()
            assistantMessage.isStreaming = false
            if (streamErrorCode === 'approval_pending') {
              runStatusStore.clearRunStatus(sessionId, messageKey)
              messageStore.removeMessages(sessionId, userMessage, assistantMessage)
              shouldRefreshPendingApprovals = true
            } else {
              runStatusStore.completeRun(sessionId, messageKey)
            }
            approvalStore.sync(sessionId)
          },
          onError(error) {
            streamStore.stop()
            assistantMessage.isStreaming = false
            const message = toFriendlyMessage(error)
            runStatusStore.failRun(sessionId, messageKey, message)
            setSessionError(sessionId, message)
            approvalStore.sync(sessionId)
          },
        })

        if (shouldRefreshPendingApprovals) await approvalStore.refreshPendingApprovals(sessionId)
      } catch (error) {
        const message = toFriendlyMessage(error)
        runStatusStore.failRun(sessionId, messageKey, message)
        setSessionError(sessionId, message)
      } finally {
        streamStore.stop()
        assistantMessage.isStreaming = false
        try {
          await sessionStore.refreshSession(sessionId)
        } catch (error) {
          console.error('Failed to refresh AgentSession state after chat.', error)
        }
        approvalStore.sync(sessionId)
      }
      return true
    })
  }

  async function submitApproval(
    callId: string,
    decision: 'approved' | 'rejected',
    chunk: ApprovalChunk,
  ) {
    const sessionId = resolvedSessionId.value
    if (!sessionId || isSessionTransitionBlocked.value) return false
    if (!ownsCurrentApproval(callId, chunk, sessionId)) {
      setSessionError(sessionId, '审批目标不属于当前会话，已阻止请求。')
      return false
    }

    return await runSessionOperation(async () => {
      let approvalFailed = false
      let approvalErrorCode: string | null = null
      clearCurrentSessionError()
      streamStore.start()
      const targetMessage = messageStore.getLastAssistantMessage(sessionId) ?? createAssistantMessage(sessionId)
      targetMessage.isStreaming = true

      try {
        await chatService.sendApprovalDecisionStream(sessionId, callId, decision, {
          onChunkReceived(incomingChunk) {
            if (incomingChunk.type === ChunkType.Error) {
              approvalFailed = true
              approvalErrorCode = getErrorCode(incomingChunk)
            }
            processChunk(targetMessage, incomingChunk, {
              setSessionError,
              onApprovalChunk: approvalStore.sync,
              onAgentSessionState: applyAgentSessionEvent,
            })
          },
          onComplete() {
            streamStore.stop()
            targetMessage.isStreaming = false
            chunk.status = approvalFailed ? getApprovalFailureStatus(approvalErrorCode) : decision
            approvalStore.sync(sessionId)
          },
          onError(error) {
            approvalFailed = true
            streamStore.stop()
            targetMessage.isStreaming = false
            chunk.status = 'pending'
            setSessionError(sessionId, toFriendlyMessage(error))
            approvalStore.sync(sessionId)
          },
        })
      } catch (error) {
        approvalFailed = true
        chunk.status = 'pending'
        setSessionError(sessionId, toFriendlyMessage(error))
      } finally {
        streamStore.stop()
        targetMessage.isStreaming = false
        try {
          await sessionStore.refreshSession(sessionId)
        } catch (error) {
          console.error('Failed to refresh AgentSession state after approval.', error)
        }

        if (approvalFailed) {
          try {
            await refreshApprovalProjection(sessionId)
          } catch (error) {
            console.error('Failed to reconcile pending approvals.', error)
          }
        }
        approvalStore.sync(sessionId)
      }

      return !approvalFailed
    })
  }

  function reset() {
    sessionStore.reset()
    messageStore.reset()
    streamStore.reset()
    approvalStore.reset()
    errorStore.reset()
    runStatusStore.reset()
    catalogStore.reset()
    historyCursors.value = {}
    isLoadingOlderHistory.value = false
    sessionOperationCount.value = 0
    initializationError.value = ''
  }

  return {
    sessions,
    currentSessionId,
    currentSession,
    agentMode,
    agentSessionStatus,
    agentSessionNotice,
    isAgentSessionUnavailable,
    canChangeAgentMode,
    composerSessionId,
    resolvedSessionId,
    isSessionActivating,
    isSessionOperationInFlight,
    isSessionTransitionBlocked,
    canEditComposerContext,
    currentMessages,
    isStreaming,
    isWaitingForApproval,
    hasPendingApproval,
    isApprovalAuthorityUnknown,
    isLoadingHistory,
    isLoadingOlderHistory,
    hasMoreHistoryBefore,
    availableKnowledgeBases,
    selectedKnowledgeBaseId,
    selectedKnowledgeBase,
    currentRunStatus,
    errorMessage,
    getRunStatusForMessage,
    prepareInitialization,
    initialize,
    loadKnowledgeBases,
    selectKnowledgeBase,
    loadOlderHistory,
    createNewSession,
    selectSession,
    deleteSession,
    clearCurrentSessionError,
    changeAgentMode,
    sendMessage,
    submitApproval,
    reset,
  }
})
