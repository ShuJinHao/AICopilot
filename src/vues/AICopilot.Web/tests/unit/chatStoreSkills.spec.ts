import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useChatStore } from '@/stores/chatStore'
import { useSessionStore } from '@/stores/sessionStore'

const chatServiceMock = vi.hoisted(() => ({
  getSkills: vi.fn(),
  getKnowledgeBases: vi.fn(),
  planAgentTask: vi.fn(),
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

const skills = [
  {
    id: 'skill-general',
    skillCode: 'general_report',
    displayName: '通用报告',
    description: '默认报告 Skill',
    allowedToolCodes: ['generate_markdown_report'],
    riskLevel: 'High',
    approvalPolicy: 'ToolApproval',
    allowedDataSourceModes: ['SimulationBusiness'],
    allowedKnowledgeScopes: [],
    outputComponentTypes: ['markdown'],
    isEnabled: true,
    isBuiltIn: true,
    version: 1,
    createdAt: '2026-06-22T07:00:00Z',
    updatedAt: '2026-06-22T07:00:00Z'
  },
  {
    id: 'skill-data',
    skillCode: 'data_analysis',
    displayName: '数据分析',
    description: '数据分析 Skill',
    allowedToolCodes: ['query_business_database_readonly'],
    riskLevel: 'High',
    approvalPolicy: 'ToolApproval',
    allowedDataSourceModes: ['SimulationBusiness'],
    allowedKnowledgeScopes: [],
    outputComponentTypes: ['chart', 'markdown'],
    isEnabled: true,
    isBuiltIn: true,
    version: 1,
    createdAt: '2026-06-22T07:00:00Z',
    updatedAt: '2026-06-22T07:00:00Z'
  },
  {
    id: 'skill-knowledge',
    skillCode: 'knowledge_research',
    displayName: '知识检索',
    description: '知识检索 Skill',
    allowedToolCodes: ['rag_search'],
    riskLevel: 'Low',
    approvalPolicy: 'FinalOutputApproval',
    allowedDataSourceModes: [],
    allowedKnowledgeScopes: ['SelectedKnowledgeBase'],
    outputComponentTypes: ['markdown'],
    isEnabled: true,
    isBuiltIn: true,
    version: 1,
    createdAt: '2026-06-22T07:00:00Z',
    updatedAt: '2026-06-22T07:00:00Z'
  }
]

const knowledgeBases = [
  {
    id: 'kb-1',
    name: '报警手册',
    description: '报警处理知识库',
    embeddingModelId: 'embedding-1',
    documentCount: 2
  },
  {
    id: 'kb-2',
    name: '设备手册',
    description: '设备维护知识库',
    embeddingModelId: 'embedding-1',
    documentCount: 3
  }
]

const plannedTask = {
  id: 'task-1',
  taskCode: 'AGT-1',
  sessionId: 'session-1',
  title: '知识检索',
  goal: '查手册',
  taskType: 'ReportGeneration',
  status: 'WaitingApproval',
  riskLevel: 'Low',
  modelId: null,
  workspaceId: null,
  workspaceCode: null,
  planJson: '{}',
  finalSummary: null,
  createdAt: '2026-06-22T07:00:00Z',
  updatedAt: '2026-06-22T07:00:00Z',
  completedAt: null,
  steps: [],
  pendingApprovalCount: 0,
  lastFailureReason: null,
  canRun: false,
  canRetry: false,
  canSubmitFinalReview: false,
  canApproveFinal: false,
  failureSummary: null,
  activeRunAttemptId: null,
  runAttemptCount: 0,
  isRunInProgress: false,
  queuedRunId: null,
  runQueueStatus: null,
  isRunQueued: false
}

describe('chatStore skills', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.stubGlobal('sessionStorage', createSessionStorageMock())
    vi.clearAllMocks()
    chatServiceMock.getSkills.mockResolvedValue(skills)
    chatServiceMock.getKnowledgeBases.mockResolvedValue(knowledgeBases)
    chatServiceMock.planAgentTask.mockResolvedValue(plannedTask)
    chatServiceMock.getAgentTaskApprovals.mockResolvedValue([])
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

  it('keeps skill selection in auto mode after loading skills', async () => {
    const store = useChatStore()

    await store.loadSkills()

    expect(store.availableSkills).toHaveLength(3)
    expect(store.selectedSkillCode).toBeNull()
    expect(store.selectedSkill).toBeNull()
    expect(store.isSkillAutoMode).toBe(true)
  })

  it('falls back to auto mode when the selection is empty or unknown', async () => {
    const store = useChatStore()
    chatServiceMock.getSkills.mockResolvedValue([...skills].reverse())

    await store.loadSkills()
    store.selectSkill('missing_skill')

    expect(store.selectedSkillCode).toBeNull()
    expect(store.selectedSkill).toBeNull()

    store.selectSkill(null)

    expect(store.selectedSkillCode).toBeNull()
    expect(store.isSkillAutoMode).toBe(true)
  })

  it('omits skill code in auto mode so the backend can route it', async () => {
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-1')
    const store = useChatStore()
    await store.loadSkills()

    await store.planAgentTask('查看 Cloud 设备日志')

    expect(chatServiceMock.planAgentTask).toHaveBeenCalledWith(expect.objectContaining({
      sessionId: 'session-1',
      goal: '查看 Cloud 设备日志',
      skillCode: null
    }))
  })

  it('sends the selected skill code when planning an agent task', async () => {
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-1')
    const store = useChatStore()
    await store.loadSkills()
    store.selectSkill('knowledge_research')

    await store.planAgentTask('查手册')

    expect(chatServiceMock.planAgentTask).toHaveBeenCalledWith(expect.objectContaining({
      sessionId: 'session-1',
      goal: '查手册',
      skillCode: 'knowledge_research'
    }))
  })

  it('sends the selected knowledge base only for skills that allow knowledge retrieval', async () => {
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-1')
    const store = useChatStore()

    await store.loadSkills()
    await store.loadKnowledgeBases()
    store.selectSkill('knowledge_research')
    store.selectKnowledgeBase('kb-2')

    await store.planAgentTask('查设备手册')

    expect(chatServiceMock.planAgentTask).toHaveBeenLastCalledWith(expect.objectContaining({
      skillCode: 'knowledge_research',
      knowledgeBaseIds: ['kb-2']
    }))

    store.selectSkill('data_analysis')

    await store.planAgentTask('分析产能')

    expect(chatServiceMock.planAgentTask).toHaveBeenLastCalledWith(expect.objectContaining({
      skillCode: 'data_analysis',
      knowledgeBaseIds: []
    }))
  })
})
