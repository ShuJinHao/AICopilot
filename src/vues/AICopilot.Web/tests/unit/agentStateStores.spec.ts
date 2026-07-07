import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '@/services/apiClient'
import { useAgentCatalogStore } from '@/stores/agentCatalogStore'
import { useAgentTaskStore } from '@/stores/agentTaskStore'
import { useArtifactWorkspaceStore } from '@/stores/artifactWorkspaceStore'
import { useChatStore } from '@/stores/chatStore'
import { useSessionStore } from '@/stores/sessionStore'

const chatServiceMock = vi.hoisted(() => ({
  getSkills: vi.fn(),
  getToolCatalog: vi.fn(),
  getKnowledgeBases: vi.fn(),
  getAgentTasksBySession: vi.fn(),
  getAgentTaskApprovals: vi.fn(),
  getAgentTaskAuditSummary: vi.fn(),
  getTimeline: vi.fn()
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

describe('agent state stores', () => {
  beforeEach(() => {
    Object.defineProperty(globalThis, 'sessionStorage', {
      value: createSessionStorageMock(),
      configurable: true
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
    store.isAgentBusy = true

    store.reset()

    expect(store.agentTasks).toEqual([])
    expect(store.agentApprovals).toEqual([])
    expect(store.agentAuditSummary).toEqual([])
    expect(store.timelineEvents).toEqual([])
    expect(store.isAgentBusy).toBe(false)
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

  it('clears session plan selections without clearing cached catalog data', () => {
    const store = useAgentCatalogStore()

    store.availableSkills = [{ skillCode: 'general_report' } as never]
    store.availablePluginTools = [{ toolCode: 'rag_search' } as never]
    store.availableKnowledgeBases = [{ id: 'kb-1' } as never]
    store.selectedSkillCode = 'general_report'
    store.selectedToolCodes = ['rag_search']
    store.selectedKnowledgeBaseId = 'kb-1'

    store.resetSelections()

    expect(store.availableSkills).toHaveLength(1)
    expect(store.availablePluginTools).toHaveLength(1)
    expect(store.availableKnowledgeBases).toHaveLength(1)
    expect(store.selectedSkillCode).toBeNull()
    expect(store.selectedToolCodes).toEqual([])
    expect(store.selectedKnowledgeBaseId).toBeNull()
  })

  it('routes catalog load failures through the current chat session error', async () => {
    chatServiceMock.getSkills.mockRejectedValue(new ApiError('API Error: 403', 403, null))
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-1')
    const store = useChatStore()

    await store.loadSkills()

    expect(store.availableSkills).toEqual([])
    expect(store.errorMessage).toBe('加载 Skill 列表失败：当前账号没有访问该功能的权限。')
  })

  it('routes task timeline load failures through the current chat session error', async () => {
    chatServiceMock.getTimeline.mockRejectedValue(new ApiError('API Error: 500', 500, {
      code: 'agent_worker_unavailable'
    }))
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-1')
    const store = useChatStore()

    await store.loadTimeline('session-1')

    expect(store.timelineEvents).toEqual([])
    expect(store.errorMessage).toBe('加载任务时间线失败：当前没有可用 DataWorker，请检查 Worker 状态。')
  })

  it('keeps stale session load failures hidden after switching sessions', async () => {
    chatServiceMock.getTimeline.mockRejectedValue(new ApiError('API Error: 500', 500, {
      code: 'agent_worker_unavailable'
    }))
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-2')
    const store = useChatStore()

    await store.loadTimeline('session-1')

    expect(store.errorMessage).toBe('')
  })
})
