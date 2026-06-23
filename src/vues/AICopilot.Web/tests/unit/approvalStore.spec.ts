import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useApprovalStore } from '@/stores/approvalStore'
import { useMessageStore } from '@/stores/messageStore'
import { useSessionStore } from '@/stores/sessionStore'
import { ChunkType, MessageRole, type FunctionApprovalRequest } from '@/types/protocols'
import type { ApprovalChunk } from '@/types/models'

const chatServiceMock = vi.hoisted(() => ({
  getPendingApprovals: vi.fn()
}))

vi.mock('@/services/chatService', () => ({
  chatService: chatServiceMock
}))

function createSessionStorageMock() {
  const state = new Map<string, string>()

  return {
    getItem(key: string) {
      return state.get(key) ?? null
    },
    setItem(key: string, value: string) {
      state.set(key, value)
    },
    removeItem(key: string) {
      state.delete(key)
    },
    clear() {
      state.clear()
    }
  }
}

function createApproval(callId: string): FunctionApprovalRequest {
  return {
    callId,
    name: 'controlled tool',
    targetType: 'McpServer',
    targetName: 'cloud-read',
    toolName: 'queryDeviceLogs',
    args: {},
    requiresOnsiteAttestation: false
  }
}

describe('approvalStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.stubGlobal('sessionStorage', createSessionStorageMock())
    vi.clearAllMocks()
  })

  it('expires stale callId approval cards when refresh no longer returns them', async () => {
    const sessionStore = useSessionStore()
    const messageStore = useMessageStore()
    const approvalStore = useApprovalStore()
    sessionStore.persistCurrentSession('session-1')

    const approval = createApproval('call-stale')
    messageStore.addMessage('session-1', {
      sessionId: 'session-1',
      role: MessageRole.Assistant,
      chunks: [
        {
          source: 'FinalAgentRunExecutor',
          type: ChunkType.ApprovalRequest,
          content: JSON.stringify(approval),
          request: approval,
          status: 'pending'
        } as ApprovalChunk
      ],
      isStreaming: false,
      timestamp: Date.now()
    })

    chatServiceMock.getPendingApprovals.mockResolvedValue([])

    await approvalStore.refreshPendingApprovals('session-1')

    expect(chatServiceMock.getPendingApprovals).toHaveBeenCalledWith('session-1')
    expect(messageStore.getApprovalChunks('session-1')[0]?.status).toBe('expired')
    expect(approvalStore.isWaitingForApproval).toBe(false)
  })
})
