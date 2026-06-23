import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useChatStore } from '@/stores/chatStore'
import { useSessionStore } from '@/stores/sessionStore'

const chatServiceMock = vi.hoisted(() => ({
  getHistory: vi.fn(),
  getPendingApprovals: vi.fn(),
  getTimeline: vi.fn(),
  decideAgentApproval: vi.fn(),
  getAgentTaskApprovals: vi.fn(),
  getAgentTasksBySession: vi.fn(),
  getAgentTaskAuditSummary: vi.fn()
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

describe('chatStore timeline', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.stubGlobal('sessionStorage', createSessionStorageMock())
    vi.clearAllMocks()
    chatServiceMock.getPendingApprovals.mockResolvedValue([])
    chatServiceMock.getAgentTaskApprovals.mockResolvedValue([])
    chatServiceMock.getAgentTasksBySession.mockResolvedValue([])
    chatServiceMock.getAgentTaskAuditSummary.mockResolvedValue([])
    chatServiceMock.getTimeline.mockResolvedValue({
      items: [],
      beforeSequence: null,
      afterSequence: null,
      hasMore: false,
      hasMoreBefore: false,
      hasMoreAfter: false
    })
  })

  it('loads session timeline events through the chat service', async () => {
    chatServiceMock.getTimeline.mockResolvedValue({
      items: [
        {
          sequence: 3,
          eventType: 'ApprovalDecided',
          createdAt: '2026-06-22T07:00:00Z',
          approvalStatus: 'Approved'
        }
      ],
      beforeSequence: 3,
      afterSequence: 3,
      hasMore: false,
      hasMoreBefore: false,
      hasMoreAfter: false
    })

    const store = useChatStore()
    await store.loadTimeline('session-1')

    expect(chatServiceMock.getTimeline).toHaveBeenCalledWith('session-1')
    expect(store.timelineEvents).toHaveLength(1)
    expect(store.timelineEvents[0]?.eventType).toBe('ApprovalDecided')
  })

  it('reloads the authoritative session timeline after an agent approval decision', async () => {
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-1')
    chatServiceMock.decideAgentApproval.mockResolvedValue({
      id: 'approval-1',
      taskId: 'task-1',
      type: 'Plan',
      status: 'Approved'
    })
    chatServiceMock.getAgentTasksBySession.mockResolvedValue([
      {
        id: 'task-1',
        sessionId: 'session-1',
        status: 'Running',
        workspaceCode: null
      }
    ])
    chatServiceMock.getTimeline.mockResolvedValue({
      items: [
        {
          sequence: 7,
          eventType: 'ApprovalDecided',
          createdAt: '2026-06-22T07:02:00Z',
          approvalRequestId: 'approval-1',
          approvalStatus: 'Approved'
        }
      ],
      beforeSequence: 7,
      afterSequence: 7,
      hasMore: false,
      hasMoreBefore: false,
      hasMoreAfter: false
    })

    const store = useChatStore()
    await store.decideAgentApproval(
      {
        id: 'approval-1',
        taskId: 'task-1',
        type: 'Plan',
        status: 'Pending'
      } as never,
      'approve',
      'ok'
    )

    expect(chatServiceMock.decideAgentApproval).toHaveBeenCalledWith('approval-1', 'approve', 'ok')
    expect(chatServiceMock.getAgentTaskApprovals).toHaveBeenCalledWith('task-1')
    expect(chatServiceMock.getTimeline).toHaveBeenCalledWith('session-1')
    expect(store.timelineEvents[0]?.approvalStatus).toBe('Approved')
  })

  it('requests older chat history with the previous sequence cursor and prepends it', async () => {
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-1')
    chatServiceMock.getHistory
      .mockResolvedValueOnce({
        items: [
          {
            messageId: 2,
            sequence: 2,
            sessionId: 'session-1',
            role: 'Assistant',
            content: 'newer',
            createdAt: '2026-06-22T07:02:00Z'
          }
        ],
        beforeSequence: 2,
        afterSequence: 2,
        hasMore: true,
        hasMoreBefore: true,
        hasMoreAfter: false
      })
      .mockResolvedValueOnce({
        items: [
          {
            messageId: 1,
            sequence: 1,
            sessionId: 'session-1',
            role: 'User',
            content: 'older',
            createdAt: '2026-06-22T07:01:00Z'
          }
        ],
        beforeSequence: 1,
        afterSequence: 1,
        hasMore: false,
        hasMoreBefore: false,
        hasMoreAfter: true
      })

    const store = useChatStore()
    await store.selectSession('session-1', true)
    const changed = await store.loadOlderHistory('session-1')

    expect(changed).toBe(true)
    expect(chatServiceMock.getHistory).toHaveBeenNthCalledWith(2, 'session-1', {
      beforeSequence: 2
    })
    expect(store.currentMessages.map((message) => message.sequence)).toEqual([1, 2])
    expect(store.hasMoreHistoryBefore).toBe(false)
  })

  it('clears timeline events when the timeline endpoint fails', async () => {
    chatServiceMock.getTimeline.mockResolvedValueOnce({
      items: [
        {
          sequence: 1,
          eventType: 'AgentTaskPlanCreated',
          createdAt: '2026-06-22T07:00:00Z'
        }
      ],
      beforeSequence: 1,
      afterSequence: 1,
      hasMore: false,
      hasMoreBefore: false,
      hasMoreAfter: false
    })
    const store = useChatStore()
    await store.loadTimeline('session-1')

    chatServiceMock.getTimeline.mockRejectedValueOnce(new Error('timeline unavailable'))
    await store.loadTimeline('session-1')

    expect(store.timelineEvents).toEqual([])
  })
})
