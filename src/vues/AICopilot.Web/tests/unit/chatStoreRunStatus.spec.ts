import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useChatStore } from '@/stores/chatStore'
import { useMessageStore } from '@/stores/messageStore'
import { useSessionStore } from '@/stores/sessionStore'
import { ChunkType, MessageRole } from '@/types/protocols'
import type { ApprovalChunk } from '@/types/models'

const chatServiceMock = vi.hoisted(() => ({
  sendMessageStream: vi.fn(),
  sendApprovalDecisionStream: vi.fn(),
  deleteSession: vi.fn(),
  getSessions: vi.fn(),
  getPendingApprovals: vi.fn(),
  getToolCatalog: vi.fn(),
  getHistory: vi.fn(),
  getAgentTasksBySession: vi.fn(),
  getAgentTaskApprovals: vi.fn(),
  getAgentTaskAuditSummary: vi.fn(),
  getTimeline: vi.fn(),
}))

vi.mock('@/services/chatService', () => ({
  chatService: chatServiceMock,
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
    },
  }
}

function activateResolvedSession(sessionId = 'session-1') {
  const sessionStore = useSessionStore()
  sessionStore.upsertSession({ id: sessionId, title: '测试会话' })
  sessionStore.persistCurrentSession(sessionId)
  sessionStore.activateSession(sessionId)
}

function createDeferred() {
  let resolve!: () => void
  const promise = new Promise<void>((complete) => {
    resolve = complete
  })

  return { promise, resolve }
}

describe('chatStore run status integration', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.stubGlobal('sessionStorage', createSessionStorageMock())
    vi.clearAllMocks()
    chatServiceMock.getSessions.mockResolvedValue([])
    chatServiceMock.getPendingApprovals.mockResolvedValue([])
    chatServiceMock.getToolCatalog.mockResolvedValue({ tools: [] })
    chatServiceMock.getHistory.mockResolvedValue({
      items: [],
      beforeSequence: null,
      afterSequence: null,
      hasMoreBefore: false,
      hasMoreAfter: false,
    })
    chatServiceMock.getAgentTasksBySession.mockResolvedValue([])
    chatServiceMock.getAgentTaskApprovals.mockResolvedValue([])
    chatServiceMock.getAgentTaskAuditSummary.mockResolvedValue([])
    chatServiceMock.getTimeline.mockResolvedValue({ items: [] })
  })

  it('starts run status before stream chunks and completes it when chat stream closes', async () => {
    activateResolvedSession()
    const store = useChatStore()

    chatServiceMock.sendMessageStream.mockImplementation(async (_sessionId, _input, callbacks) => {
      const assistantMessage = store.currentMessages[1]!

      expect(assistantMessage.role).toBe(MessageRole.Assistant)
      expect(store.getRunStatusForMessage(assistantMessage)).toMatchObject({
        phase: 'understanding',
        summary: '正在理解问题',
      })

      callbacks.onChunkReceived({
        source: 'IntentRoutingExecutor',
        type: ChunkType.Intent,
        content: JSON.stringify([{ intent: 'Analysis.DeviceLog.Query', confidence: 0.92 }]),
      })
      expect(store.getRunStatusForMessage(assistantMessage)).toMatchObject({
        phase: 'querying',
        summary: '正在准备 Cloud 只读查询',
      })

      callbacks.onChunkReceived({
        source: 'DataAnalysisExecutor',
        type: ChunkType.FunctionResult,
        content: JSON.stringify({
          id: 'call-1',
          name: 'queryDeviceLogs',
          result: JSON.stringify({ rowCount: 20 }),
        }),
      })
      expect(store.getRunStatusForMessage(assistantMessage)).toMatchObject({
        phase: 'querying',
        queryCount: 1,
        returnedRows: 20,
      })

      callbacks.onChunkReceived({
        source: 'FinalAgentRunExecutor',
        type: ChunkType.Text,
        content: '最近 1 天有错误和警告日志。',
      })
      expect(store.getRunStatusForMessage(assistantMessage)).toMatchObject({
        phase: 'answering',
        summary: '正在生成回答',
      })

      callbacks.onComplete()
    })

    await store.sendMessage('查询最近 1 天日志')

    const assistantMessage = store.currentMessages[1]!
    expect(store.getRunStatusForMessage(assistantMessage)).toMatchObject({
      phase: 'completed',
      summary: '回答已完成',
      queryCount: 1,
      returnedRows: 20,
    })
    expect(assistantMessage.isStreaming).toBe(false)
  })

  it('does not send a message through an unresolved persisted session id', async () => {
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('missing-session')
    const store = useChatStore()

    await store.sendMessage('这条消息不能发给陈旧会话')

    expect(chatServiceMock.sendMessageStream).not.toHaveBeenCalled()
    expect(store.currentMessages).toEqual([])
  })

  it('revokes retained session action authority before chat view initialization', async () => {
    activateResolvedSession()
    const store = useChatStore()

    expect(store.resolvedSessionId).toBe('session-1')

    store.prepareInitialization()
    await store.sendMessage('重新进入页面时不能沿用旧会话动作权限')

    expect(store.isSessionActivating).toBe(true)
    expect(store.resolvedSessionId).toBeNull()
    expect(chatServiceMock.sendMessageStream).not.toHaveBeenCalled()
  })

  it('blocks session transitions while a chat stream is in flight', async () => {
    activateResolvedSession()
    const sessionStore = useSessionStore()
    sessionStore.upsertSession({ id: 'session-2', title: '第二会话' })
    const stream = createDeferred()
    let completeStream!: () => void
    chatServiceMock.sendMessageStream.mockImplementation((_sessionId, _input, callbacks) => {
      completeStream = () => {
        callbacks.onComplete()
        stream.resolve()
      }
      return stream.promise
    })
    const store = useChatStore()

    const sendPromise = store.sendMessage('A 会话流式请求')
    expect(store.isStreaming).toBe(true)
    expect(store.isSessionTransitionBlocked).toBe(true)

    await store.selectSession('session-2')

    expect(store.currentSessionId).toBe('session-1')
    expect(store.resolvedSessionId).toBe('session-1')

    completeStream()
    await sendPromise

    expect(store.isStreaming).toBe(false)
    expect(store.isSessionTransitionBlocked).toBe(false)
  })

  it('keeps approval refresh inside the originating chat operation gate', async () => {
    activateResolvedSession()
    const sessionStore = useSessionStore()
    sessionStore.upsertSession({ id: 'session-2', title: '第二会话' })
    let signalPendingRequest!: () => void
    const pendingRequested = new Promise<void>((resolve) => {
      signalPendingRequest = resolve
    })
    let resolvePendingApprovals!: (value: never[]) => void
    chatServiceMock.getPendingApprovals.mockImplementation(() => {
      signalPendingRequest()
      return new Promise<never[]>((resolve) => {
        resolvePendingApprovals = resolve
      })
    })
    chatServiceMock.sendMessageStream.mockImplementation(async (_sessionId, _input, callbacks) => {
      callbacks.onChunkReceived({
        source: 'FinalAgentRunExecutor',
        type: ChunkType.Error,
        content: JSON.stringify({ code: 'approval_pending' }),
      })
      callbacks.onComplete()
    })
    const store = useChatStore()

    const sendPromise = store.sendMessage('触发待审批')
    await pendingRequested

    expect(store.isStreaming).toBe(false)
    expect(store.isSessionOperationInFlight).toBe(true)
    expect(store.isSessionTransitionBlocked).toBe(true)
    await store.selectSession('session-2')
    expect(store.currentSessionId).toBe('session-1')

    resolvePendingApprovals([])
    await sendPromise

    expect(store.isSessionOperationInFlight).toBe(false)
    expect(store.isSessionTransitionBlocked).toBe(false)
    expect(store.resolvedSessionId).toBe('session-1')
  })

  it('keeps sending fail-closed when a newly discovered approval projection cannot refresh', async () => {
    activateResolvedSession()
    chatServiceMock.sendMessageStream.mockImplementation(async (_sessionId, _input, callbacks) => {
      callbacks.onChunkReceived({
        source: 'FinalAgentRunExecutor',
        type: ChunkType.Error,
        content: JSON.stringify({ code: 'approval_pending' }),
      })
      callbacks.onComplete()
    })
    chatServiceMock.getPendingApprovals.mockRejectedValueOnce(
      new Error('approval projection unavailable'),
    )
    const store = useChatStore()

    await store.sendMessage('首次发现待审批')
    await store.sendMessage('审批状态未知时不能再次发送')

    expect(chatServiceMock.sendMessageStream).toHaveBeenCalledOnce()
    expect(store.isWaitingForApproval).toBe(true)
    expect(store.errorMessage).toBe('请求失败，请稍后重试。')
  })

  it('blocks a stale function approval that is not owned by the current session', async () => {
    activateResolvedSession()
    const store = useChatStore()
    const request = {
      callId: 'call-a',
      name: 'controlled tool',
      targetType: 'McpServer',
      targetName: 'cloud-read',
      toolName: 'queryDeviceLogs',
      args: {},
      requiresOnsiteAttestation: false,
    }
    const chunk = {
      source: 'FinalAgentRunExecutor',
      type: ChunkType.ApprovalRequest,
      content: JSON.stringify(request),
      request,
      status: 'pending',
    } as const

    const submitted = await store.submitApproval('call-from-session-b', 'approved', false, chunk)

    expect(submitted).toBe(false)
    expect(chatServiceMock.sendApprovalDecisionStream).not.toHaveBeenCalled()
    expect(store.errorMessage).toBe('操作目标不属于当前会话，已阻止请求。')
  })

  it('submits a pending function approval obtained from the reactive current session projection', async () => {
    activateResolvedSession()
    const messageStore = useMessageStore()
    const request = {
      callId: 'call-a',
      name: 'controlled tool',
      targetType: 'McpServer',
      targetName: 'cloud-read',
      toolName: 'queryDeviceLogs',
      args: {},
      requiresOnsiteAttestation: false,
    }
    messageStore.addMessage('session-1', {
      sessionId: 'session-1',
      role: MessageRole.Assistant,
      chunks: [
        {
          source: 'FinalAgentRunExecutor',
          type: ChunkType.ApprovalRequest,
          content: JSON.stringify(request),
          request,
          status: 'pending',
        } as ApprovalChunk,
      ],
      isStreaming: false,
      timestamp: Date.now(),
    })
    chatServiceMock.sendApprovalDecisionStream.mockImplementation(
      async (_sessionId, _callId, _decision, _onsiteConfirmed, _request, callbacks) => {
        callbacks.onComplete()
      },
    )
    const store = useChatStore()
    const chunk = store.currentMessages[0]!.chunks[0] as ApprovalChunk

    const submitted = await store.submitApproval('call-a', 'approved', false, chunk)

    expect(submitted).toBe(true)
    expect(chatServiceMock.sendApprovalDecisionStream).toHaveBeenCalledOnce()
    expect(chunk.status).toBe('approved')
  })

  it('keeps deletion inside the session transition critical section', async () => {
    activateResolvedSession()
    const sessionStore = useSessionStore()
    sessionStore.upsertSession({ id: 'session-2', title: '第二会话' })
    const deletion = createDeferred()
    chatServiceMock.deleteSession.mockReturnValue(deletion.promise)
    const store = useChatStore()

    const deletePromise = store.deleteSession('session-2')

    expect(store.resolvedSessionId).toBeNull()
    expect(store.isSessionTransitionBlocked).toBe(true)
    await store.selectSession('session-2')
    await store.sendMessage('删除等待期间不能写入')

    expect(store.currentSessionId).toBe('session-1')
    expect(chatServiceMock.sendMessageStream).not.toHaveBeenCalled()

    deletion.resolve()
    await deletePromise

    expect(store.resolvedSessionId).toBe('session-1')
    expect(store.isSessionTransitionBlocked).toBe(false)
    expect(store.sessions.map((session) => session.id)).toEqual(['session-1'])
  })

  it('restores the active session when deletion fails', async () => {
    activateResolvedSession()
    const sessionStore = useSessionStore()
    sessionStore.upsertSession({ id: 'session-2', title: '第二会话' })
    chatServiceMock.deleteSession.mockRejectedValue(new Error('delete unavailable'))
    chatServiceMock.getSessions.mockResolvedValueOnce([
      { id: 'session-2', title: '第二会话' },
      { id: 'session-1', title: '测试会话' },
    ])
    const store = useChatStore()

    const deleted = await store.deleteSession('session-2')

    expect(deleted).toBe(false)
    expect(store.currentSessionId).toBe('session-1')
    expect(store.resolvedSessionId).toBe('session-1')
    expect(store.isSessionTransitionBlocked).toBe(false)
    expect(store.sessions.map((session) => session.id)).toEqual(['session-2', 'session-1'])
    expect(store.errorMessage).toBe('请求失败，请稍后重试。')
  })

  it('treats a timed-out deletion as committed when reconciliation no longer finds the session', async () => {
    activateResolvedSession()
    const sessionStore = useSessionStore()
    sessionStore.upsertSession({ id: 'session-2', title: '第二会话' })
    chatServiceMock.deleteSession.mockRejectedValue(new Error('delete response lost'))
    chatServiceMock.getSessions.mockResolvedValueOnce([{ id: 'session-2', title: '第二会话' }])
    const store = useChatStore()

    const deleted = await store.deleteSession('session-1')

    expect(deleted).toBe(true)
    expect(store.sessions.map((session) => session.id)).toEqual(['session-2'])
    expect(store.currentSessionId).toBe('session-2')
    expect(store.resolvedSessionId).toBe('session-2')
    expect(store.isSessionTransitionBlocked).toBe(false)
  })

  it('keeps action authority unresolved when deletion outcome reconciliation also fails', async () => {
    activateResolvedSession()
    chatServiceMock.deleteSession.mockRejectedValue(new Error('delete response lost'))
    chatServiceMock.getSessions.mockRejectedValueOnce(new Error('session list unavailable'))
    const store = useChatStore()

    const deleted = await store.deleteSession('session-1')

    expect(deleted).toBe(false)
    expect(store.currentSessionId).toBe('session-1')
    expect(store.resolvedSessionId).toBeNull()
    expect(store.isSessionTransitionBlocked).toBe(false)
    expect(store.errorMessage).toContain('删除结果无法确认，已暂停会话操作')
    await store.sendMessage('不能写入结果未知的会话')
    expect(chatServiceMock.sendMessageStream).not.toHaveBeenCalled()
  })
})
