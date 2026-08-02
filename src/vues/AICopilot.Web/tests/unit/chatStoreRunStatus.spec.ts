import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ChunkType, MessageRole, type Session } from '@/types/protocols'
import type { ApprovalChunk } from '@/types/models'
import { useApprovalStore } from '@/stores/approvalStore'
import { useChatStore } from '@/stores/chatStore'
import { useMessageStore } from '@/stores/messageStore'
import { useSessionStore } from '@/stores/sessionStore'

const chatServiceMock = vi.hoisted(() => ({
  getSessions: vi.fn(),
  createSession: vi.fn(),
  getSession: vi.fn(),
  updateAgentMode: vi.fn(),
  deleteSession: vi.fn(),
  getHistory: vi.fn(),
  getKnowledgeBases: vi.fn(),
  getPendingApprovals: vi.fn(),
  sendMessageStream: vi.fn(),
  sendApprovalDecisionStream: vi.fn(),
}))

vi.mock('@/services/chatService', () => ({ chatService: chatServiceMock }))

function createSessionStorageMock() {
  const values = new Map<string, string>()
  return {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => values.set(key, value),
    removeItem: (key: string) => values.delete(key),
    clear: () => values.clear(),
  }
}

function readySession(overrides: Partial<Session> = {}): Session {
  return {
    id: 'session-1',
    title: '当前会话',
    agentMode: 'execute',
    agentSessionVersion: 3,
    agentSessionStatus: 'Ready',
    agentSessionResetRequired: false,
    hasPendingApproval: false,
    ...overrides,
  }
}

function activate(session: Session) {
  const sessionStore = useSessionStore()
  sessionStore.sessions = [session]
  sessionStore.persistCurrentSession(session.id)
  sessionStore.completeSessionActivation(session.id)
  useApprovalStore().sync(session.id)
}

function addApprovalCard() {
  const messageStore = useMessageStore()
  const approval = {
    callId: 'call-1',
    name: 'queryDeviceLogs',
    targetType: 'McpServer',
    targetName: 'cloud-read',
    toolName: 'queryDeviceLogs',
    args: { deviceCode: 'DEV-001' },
  }
  const chunk = {
    source: 'HarnessAgent',
    type: ChunkType.ApprovalRequest,
    content: JSON.stringify(approval),
    request: approval,
    status: 'pending',
  } as ApprovalChunk
  messageStore.addMessage('session-1', {
    sessionId: 'session-1',
    role: MessageRole.Assistant,
    chunks: [chunk],
    isStreaming: false,
    timestamp: 1,
  })
  return messageStore.getApprovalChunks('session-1')[0]!
}

describe('chatStore current Harness lifecycle', () => {
  beforeEach(() => {
    vi.stubGlobal('sessionStorage', createSessionStorageMock())
    setActivePinia(createPinia())
    vi.clearAllMocks()
    chatServiceMock.getSession.mockResolvedValue(readySession())
    chatServiceMock.getPendingApprovals.mockResolvedValue([])
    chatServiceMock.getKnowledgeBases.mockResolvedValue([])
  })

  it('tracks a chat turn from a real tool call through final answer completion', async () => {
    activate(readySession())
    chatServiceMock.sendMessageStream.mockImplementation(async (_sessionId, _message, callbacks) => {
      callbacks.onChunkReceived({
        source: 'HarnessAgent',
        type: ChunkType.FunctionCall,
        content: JSON.stringify({ id: 'call-1', name: 'queryDeviceLogs', args: '{}' }),
      })
      callbacks.onChunkReceived({
        source: 'HarnessAgent',
        type: ChunkType.FunctionResult,
        content: JSON.stringify({ id: 'call-1', result: { rows: [{ id: 1 }] } }),
      })
      callbacks.onChunkReceived({
        source: 'HarnessAgent',
        type: ChunkType.Text,
        content: '查询完成。',
      })
      callbacks.onComplete()
    })

    const store = useChatStore()
    expect(await store.sendMessage('查询设备日志')).toBe(true)

    const assistant = store.currentMessages.at(-1)!
    expect(assistant.role).toBe(MessageRole.Assistant)
    expect(assistant.isStreaming).toBe(false)
    expect(store.getRunStatusForMessage(assistant)).toMatchObject({
      phase: 'completed',
      queryCount: 1,
      returnedRows: 1,
    })
  })

  it('submits a protected decision without client-supplied tool identity', async () => {
    activate(readySession({ hasPendingApproval: true }))
    const chunk = addApprovalCard()
    useApprovalStore().sync('session-1')
    chatServiceMock.sendApprovalDecisionStream.mockImplementation(
      async (sessionId, callId, decision, callbacks) => {
        expect({ sessionId, callId, decision }).toEqual({
          sessionId: 'session-1',
          callId: 'call-1',
          decision: 'approved',
        })
        callbacks.onChunkReceived({
          source: 'HarnessAgent',
          type: ChunkType.Text,
          content: '已继续执行。',
        })
        callbacks.onComplete()
      },
    )

    const store = useChatStore()
    expect(await store.submitApproval('call-1', 'approved', chunk)).toBe(true)
    expect(chunk.status).toBe('approved')
    expect(chatServiceMock.sendApprovalDecisionStream).toHaveBeenCalledOnce()
  })

  it('expires local approval authority when the server interrupts the session', async () => {
    activate(readySession({ hasPendingApproval: true }))
    const chunk = addApprovalCard()
    useApprovalStore().sync('session-1')
    chatServiceMock.sendApprovalDecisionStream.mockImplementation(
      async (_sessionId, _callId, _decision, callbacks) => {
        callbacks.onChunkReceived({
          source: 'HarnessAgent',
          type: ChunkType.Error,
          content: JSON.stringify({ code: 'agent_session_interrupted' }),
        })
        callbacks.onComplete()
      },
    )
    chatServiceMock.getSession.mockResolvedValue(
      readySession({
        agentSessionStatus: 'Interrupted',
        hasPendingApproval: false,
      }),
    )

    const store = useChatStore()
    expect(await store.submitApproval('call-1', 'approved', chunk)).toBe(false)
    expect(chunk.status).toBe('expired')
    expect(store.agentSessionStatus).toBe('Interrupted')
    expect(store.hasPendingApproval).toBe(false)
  })

  it('blocks new turns for an interrupted session', async () => {
    activate(readySession({ agentSessionStatus: 'Interrupted' }))
    const store = useChatStore()

    expect(await store.sendMessage('不应发出')).toBe(false)
    expect(chatServiceMock.sendMessageStream).not.toHaveBeenCalled()
    expect(store.agentSessionNotice).toContain('新建会话')
  })
})
