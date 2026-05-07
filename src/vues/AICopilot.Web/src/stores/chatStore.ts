import { computed } from 'vue'
import { defineStore } from 'pinia'
import { chatService } from '@/services/chatService'
import { ChunkType, MessageRole } from '@/types/protocols'
import type { ApprovalChunk } from '@/types/models'
import { processChunk, getErrorCode } from '@/protocol/chunkReducer'
import { getApprovalFailureStatus } from '@/protocol/approvalProtocol'
import { useApprovalStore } from './approvalStore'
import { useChatErrorStore, toFriendlyMessage } from './chatErrorStore'
import { useMessageStore } from './messageStore'
import { useSessionStore } from './sessionStore'
import { useStreamStore } from './streamStore'

export const useChatStore = defineStore('chat', () => {
  const sessionStore = useSessionStore()
  const messageStore = useMessageStore()
  const streamStore = useStreamStore()
  const approvalStore = useApprovalStore()
  const errorStore = useChatErrorStore()

  const sessions = computed(() => sessionStore.sessions)
  const currentSessionId = computed(() => sessionStore.currentSessionId)
  const currentSession = computed(() => sessionStore.currentSession)
  const currentMessages = computed(() => messageStore.currentMessages)
  const isStreaming = computed(() => streamStore.isStreaming)
  const isWaitingForApproval = computed(() => approvalStore.isWaitingForApproval)
  const isLoadingHistory = computed(() => sessionStore.isLoadingHistory)
  const errorMessage = computed(() => errorStore.errorMessage)

  function bindErrorSession() {
    errorStore.bindCurrentSession(sessionStore.currentSessionId)
  }

  async function loadHistory(sessionId: string, force = false) {
    if (!force && messageStore.messagesMap[sessionId]?.length) {
      await approvalStore.refreshPendingApprovals(sessionId)
      return
    }

    sessionStore.isLoadingHistory = true
    try {
      const history = await chatService.getHistory(sessionId)
      messageStore.setHistory(sessionId, history)
      await approvalStore.refreshPendingApprovals(sessionId)
    } finally {
      sessionStore.isLoadingHistory = false
    }
  }

  async function initialize() {
    errorStore.clearSessionError()
    await sessionStore.loadSessions()

    if (sessionStore.sessions.length === 0) {
      await createNewSession()
      return
    }

    const restoredSessionId = sessionStore.currentSessionId
    const initialSession =
      sessionStore.sessions.find((session) => session.id === restoredSessionId) ??
      sessionStore.sessions[0] ??
      null

    if (!initialSession) {
      sessionStore.persistCurrentSession(null)
      bindErrorSession()
      return
    }

    await selectSession(initialSession.id)
  }

  async function createNewSession() {
    errorStore.clearSessionError()
    const newSession = await sessionStore.createSession()
    messageStore.messagesMap[newSession.id] = []
    streamStore.stop()
    approvalStore.sync(newSession.id)
    bindErrorSession()
    return newSession
  }

  async function selectSession(id: string, forceReload = false) {
    sessionStore.persistCurrentSession(id)
    bindErrorSession()
    errorStore.clearSessionError(id)
    approvalStore.sync(id)
    await loadHistory(id, forceReload)
  }

  async function confirmOnsitePresence(expiresInMinutes = 30) {
    if (!sessionStore.currentSessionId) {
      return
    }

    errorStore.clearSessionError(sessionStore.currentSessionId)
    const updatedSession = await chatService.updateSessionSafetyAttestation(
      sessionStore.currentSessionId,
      true,
      expiresInMinutes
    )
    sessionStore.upsertSession(updatedSession)
    await approvalStore.refreshPendingApprovals(updatedSession.id)
    return updatedSession
  }

  async function clearOnsitePresence() {
    if (!sessionStore.currentSessionId) {
      return
    }

    errorStore.clearSessionError(sessionStore.currentSessionId)
    const updatedSession = await chatService.updateSessionSafetyAttestation(
      sessionStore.currentSessionId,
      false
    )
    sessionStore.upsertSession(updatedSession)
    await approvalStore.refreshPendingApprovals(updatedSession.id)
    return updatedSession
  }

  async function sendMessage(input: string) {
    if (!sessionStore.currentSessionId || streamStore.isStreaming || approvalStore.isWaitingForApproval) {
      return
    }

    const sessionId = sessionStore.currentSessionId
    errorStore.clearSessionError(sessionId)

    const userMessage = messageStore.addMessage(sessionId, {
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

    const assistantMessage = messageStore.addMessage(sessionId, {
      sessionId,
      role: MessageRole.Assistant,
      chunks: [],
      isStreaming: true,
      timestamp: Date.now()
    })

    streamStore.start()
    let streamErrorCode: string | null = null

    try {
      await chatService.sendMessageStream(sessionId, input, {
        onChunkReceived(chunk) {
          if (chunk.type === ChunkType.Error) {
            streamErrorCode = getErrorCode(chunk)
          }

          processChunk(assistantMessage, chunk, {
            setSessionError: errorStore.setSessionError,
            onApprovalChunk: approvalStore.sync
          })
        },
        onComplete() {
          streamStore.stop()
          assistantMessage.isStreaming = false
          if (streamErrorCode === 'approval_pending') {
            messageStore.removeMessages(sessionId, userMessage, assistantMessage)
            void approvalStore.refreshPendingApprovals(sessionId)
          }

          approvalStore.sync(sessionId)
        },
        onError(error) {
          streamStore.stop()
          assistantMessage.isStreaming = false
          errorStore.setSessionError(sessionId, toFriendlyMessage(error))
          approvalStore.sync(sessionId)
        }
      })
    } catch (error) {
      errorStore.setSessionError(sessionId, toFriendlyMessage(error))
    } finally {
      streamStore.stop()
      assistantMessage.isStreaming = false
      approvalStore.sync(sessionId)
    }
  }

  async function submitApproval(
    callId: string,
    decision: 'approved' | 'rejected',
    onsiteConfirmed: boolean,
    chunk: ApprovalChunk
  ) {
    if (!sessionStore.currentSessionId || streamStore.isStreaming) {
      return false
    }

    const sessionId = sessionStore.currentSessionId
    let approvalFailed = false
    let approvalErrorCode: string | null = null
    errorStore.clearSessionError(sessionId)
    streamStore.start()

    let targetMessage = messageStore.getLastAssistantMessage(sessionId)
    if (!targetMessage) {
      targetMessage = messageStore.addMessage(sessionId, {
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

          processChunk(targetMessage!, incomingChunk, {
            setSessionError: errorStore.setSessionError,
            onApprovalChunk: approvalStore.sync
          })
        },
        onComplete() {
          streamStore.stop()
          if (targetMessage) {
            targetMessage.isStreaming = false
          }

          chunk.status = approvalFailed ? getApprovalFailureStatus(approvalErrorCode) : decision
          approvalStore.sync(sessionId)
        },
        onError(error) {
          approvalFailed = true
          streamStore.stop()
          if (targetMessage) {
            targetMessage.isStreaming = false
          }

          chunk.status = 'pending'
          errorStore.setSessionError(sessionId, toFriendlyMessage(error))
          approvalStore.sync(sessionId)
        }
      })
    } catch (error) {
      approvalFailed = true
      chunk.status = 'pending'
      errorStore.setSessionError(sessionId, toFriendlyMessage(error))
    } finally {
      streamStore.stop()
      if (targetMessage) {
        targetMessage.isStreaming = false
      }

      if (approvalFailed) {
        await approvalStore.refreshPendingApprovals(sessionId)
      }

      approvalStore.sync(sessionId)
    }

    return !approvalFailed
  }

  function reset() {
    sessionStore.reset()
    messageStore.reset()
    streamStore.reset()
    approvalStore.reset()
    errorStore.reset()
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
