import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '@/services/apiClient'
import { useAgentCatalogStore } from '@/stores/agentCatalogStore'
import { useAgentTaskStore } from '@/stores/agentTaskStore'
import { useArtifactWorkspaceStore } from '@/stores/artifactWorkspaceStore'
import { useChatStore } from '@/stores/chatStore'
import { useSessionStore } from '@/stores/sessionStore'

const chatServiceMock = vi.hoisted(() => ({
  getKnowledgeBases: vi.fn(),
  getAgentTasksBySession: vi.fn(),
  getAgentTaskApprovals: vi.fn(),
  getAgentTaskAuditSummary: vi.fn(),
  getAgentTaskRuntimeSnapshot: vi.fn(),
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

describe('agent state stores', () => {
  beforeEach(() => {
    Object.defineProperty(globalThis, 'sessionStorage', {
      value: createSessionStorageMock(),
      configurable: true,
    })
    vi.clearAllMocks()
    setActivePinia(createPinia())
  })

  it('resets task runtime state through agentTaskStore', () => {
    const store = useAgentTaskStore()

    store.agentTasks = [{ id: 'task-1' } as never]
    store.agentApprovals = [{ id: 'approval-1', status: 'Pending' } as never]
    store.agentAuditSummary = [{ id: 'audit-1' } as never]
    store.timelineEvents = [{ sequence: 1, eventType: 'AgentTaskPlanCreated' } as never]
    store.runtimeSnapshot = { taskId: 'task-1', nodes: [], evidence: [], metrics: [] } as never
    store.isAgentBusy = true

    store.reset()

    expect(store.agentTasks).toEqual([])
    expect(store.agentApprovals).toEqual([])
    expect(store.agentAuditSummary).toEqual([])
    expect(store.timelineEvents).toEqual([])
    expect(store.runtimeSnapshot).toBeNull()
    expect(store.isAgentBusy).toBe(false)
  })

  it('loads only the typed safe runtime snapshot projection', async () => {
    chatServiceMock.getAgentTaskRuntimeSnapshot.mockResolvedValue({
      taskId: 'task-1',
      runAttemptId: 'attempt-1',
      status: 'Running',
      generatedAt: '2026-07-22T08:00:00Z',
      nodes: [{ nodeId: 'node-1', label: 'Cloud 只读查询', kind: 'CloudRead', status: 'Completed' }],
      evidence: [{ nodeId: 'node-1', truthClass: 'ObservedFact', truthLabel: '事实', findings: [] }],
      metrics: [{ code: 'queue_wait_ms', label: '排队等待', value: 12, unit: 'ms', status: 'Recorded' }],
    })
    const store = useAgentTaskStore()

    await store.loadRuntimeSnapshot('task-1')

    expect(store.runtimeSnapshot?.taskId).toBe('task-1')
    expect(store.runtimeSnapshot?.evidence[0]?.truthClass).toBe('ObservedFact')
    expect(store.runtimeSnapshot).not.toHaveProperty('rawToolArguments')
    expect(store.runtimeSnapshot).not.toHaveProperty('sql')
    expect(store.runtimeSnapshot).not.toHaveProperty('reasoning')
  })

  it('resets artifact workspace state through artifactWorkspaceStore', () => {
    const store = useArtifactWorkspaceStore()

    store.uploadedFiles = [{ id: 'upload-1' } as never]
    store.currentWorkspace = { workspaceCode: 'ws-1' } as never
    store.currentArtifactPreview = { artifactId: 'artifact-1' } as never
    store.chartPreview = { labels: ['A'], values: [1] }

    store.reset()

    expect(store.uploadedFiles).toEqual([])
    expect(store.currentWorkspace).toBeNull()
    expect(store.currentArtifactPreview).toBeNull()
    expect(store.chartPreview).toBeNull()
  })

  it('keeps the knowledge catalog while clearing the selected knowledge scope', () => {
    const store = useAgentCatalogStore()

    store.availableKnowledgeBases = [{ id: 'kb-1' } as never]
    store.selectedKnowledgeBaseId = 'kb-1'

    store.resetSelections()

    expect(store.availableKnowledgeBases).toHaveLength(1)
    expect(store.selectedKnowledgeBaseId).toBeNull()
  })

  it('routes task timeline load failures through the current chat session error', async () => {
    chatServiceMock.getTimeline.mockRejectedValue(
      new ApiError('API Error: 500', 500, {
        code: 'agent_worker_unavailable',
      }),
    )
    const sessionStore = useSessionStore()
    sessionStore.upsertSession({ id: 'session-1', title: '测试会话' })
    sessionStore.persistCurrentSession('session-1')
    sessionStore.activateSession('session-1')
    const store = useChatStore()

    await store.loadTimeline('session-1')

    expect(store.timelineEvents).toEqual([])
    expect(store.errorMessage).toBe(
      '加载任务时间线失败：当前没有可用 DataWorker，请检查 Worker 状态。',
    )
  })

  it('rejects stale timeline facade calls after switching sessions', async () => {
    chatServiceMock.getTimeline.mockRejectedValue(
      new ApiError('API Error: 500', 500, {
        code: 'agent_worker_unavailable',
      }),
    )
    const sessionStore = useSessionStore()
    sessionStore.upsertSession({ id: 'session-1', title: '旧会话' })
    sessionStore.upsertSession({ id: 'session-2', title: '当前会话' })
    sessionStore.persistCurrentSession('session-2')
    sessionStore.activateSession('session-2')
    const store = useChatStore()

    await store.loadTimeline('session-1')

    expect(chatServiceMock.getTimeline).not.toHaveBeenCalled()
    expect(store.errorMessage).toBe('操作目标不属于当前会话，已阻止请求。')
  })
})
