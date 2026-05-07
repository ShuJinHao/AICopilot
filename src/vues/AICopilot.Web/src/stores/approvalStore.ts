import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { ChunkType, MessageRole, type FunctionApprovalRequest } from '@/types/protocols'
import type { ApprovalChunk } from '@/types/models'
import { chatService } from '@/services/chatService'
import { useMessageStore } from './messageStore'
import { useSessionStore } from './sessionStore'
import { useChatErrorStore, toFriendlyMessage } from './chatErrorStore'

export const useApprovalStore = defineStore('chatApproval', () => {
  const sessionStore = useSessionStore()
  const messageStore = useMessageStore()
  const errorStore = useChatErrorStore()
  const activeSessionId = ref<string | null>(null)

  const isWaitingForApproval = computed(() => {
    const sessionId = activeSessionId.value
    if (!sessionId || sessionId !== sessionStore.currentSessionId) {
      return false
    }

    return messageStore.getApprovalChunks(sessionId).some((chunk) => chunk.status === 'pending')
  })

  function sync(sessionId: string | null = sessionStore.currentSessionId) {
    activeSessionId.value = sessionId
  }

  async function refreshPendingApprovals(sessionId: string) {
    try {
      const pendingApprovals = await chatService.getPendingApprovals(sessionId)
      reconcilePendingApprovalCards(sessionId, pendingApprovals)
    } catch (error) {
      errorStore.setSessionError(sessionId, toFriendlyMessage(error))
    } finally {
      sync(sessionId)
    }
  }

  function reconcilePendingApprovalCards(
    sessionId: string,
    pendingApprovals: FunctionApprovalRequest[]
  ) {
    const pendingIds = new Set(pendingApprovals.map((approval) => approval.callId))
    const existingChunks = messageStore.getApprovalChunks(sessionId)

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
      sync(sessionId)
      return
    }

    messageStore.addMessage(sessionId, {
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
    sync(sessionId)
  }

  function reset() {
    activeSessionId.value = null
  }

  return {
    isWaitingForApproval,
    sync,
    refreshPendingApprovals,
    reconcilePendingApprovalCards,
    reset
  }
})
