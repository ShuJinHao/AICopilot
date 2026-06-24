import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '@/services/apiClient'
import { useChatStore } from '@/stores/chatStore'
import { useSessionStore } from '@/stores/sessionStore'

const chatServiceMock = vi.hoisted(() => ({
  getSkills: vi.fn(),
  getToolCatalog: vi.fn(),
  getKnowledgeBases: vi.fn(),
  planAgentTask: vi.fn(),
  approveAgentTaskPlan: vi.fn(),
  decideAgentApproval: vi.fn(),
  runAgentTask: vi.fn(),
  retryAgentTask: vi.fn(),
  getAgentTasksBySession: vi.fn(),
  getAgentTaskApprovals: vi.fn(),
  getAgentTaskAuditSummary: vi.fn(),
  getTimeline: vi.fn(),
  downloadArtifact: vi.fn()
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

const toolCatalog = {
  version: 1,
  availableToolCount: 2,
  mockMcpOnly: true,
  riskSummary: { Low: 2 },
  tools: [
    {
      toolCode: 'rag_search',
      displayName: '知识库检索',
      description: '检索已授权知识库',
      providerType: 'BuiltIn',
      targetType: 'AgentRuntime',
      targetName: 'AgentTaskRuntime',
      inputSchemaJson: '{}',
      requiresApproval: false,
      riskLevel: 'Low',
      timeoutSeconds: 120,
      auditLevel: 'Standard',
      runtimeAvailable: true,
      inputSchema: null,
      outputSchema: null,
      category: 'Knowledge',
      businessDomains: [],
      dataBoundary: 'NoData',
      isVisibleToPlanner: true,
      isExecutableByAgent: true,
      schemaVersion: 1,
      catalogVersion: 1,
      approvalPolicy: 'None',
      providerKind: 'BuiltIn',
      isMock: false
    },
    {
      toolCode: 'generate_markdown_report',
      displayName: '报告生成',
      description: '生成 Markdown 报告',
      providerType: 'BuiltIn',
      targetType: 'AgentRuntime',
      targetName: 'AgentTaskRuntime',
      inputSchemaJson: '{}',
      requiresApproval: false,
      riskLevel: 'Low',
      timeoutSeconds: 120,
      auditLevel: 'Standard',
      runtimeAvailable: true,
      inputSchema: null,
      outputSchema: null,
      category: 'Artifact',
      businessDomains: [],
      dataBoundary: 'NoData',
      isVisibleToPlanner: true,
      isExecutableByAgent: true,
      schemaVersion: 1,
      catalogVersion: 1,
      approvalPolicy: 'None',
      providerKind: 'BuiltIn',
      isMock: false
    }
  ]
}

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

function createTask(overrides: Partial<typeof plannedTask> = {}) {
  return {
    ...plannedTask,
    ...overrides
  }
}

function createPlanApproval(overrides = {}) {
  return {
    id: 'approval-1',
    taskId: 'task-1',
    targetId: 'task-1',
    type: 'Plan',
    status: 'Pending',
    targetName: '计划确认',
    reason: '执行前确认计划',
    riskLevel: 'Low',
    requestedAt: '2026-06-22T07:00:00Z',
    decidedAt: null,
    decidedBy: null,
    decision: null,
    comment: null,
    ...overrides
  }
}

describe('chatStore skills', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.stubGlobal('sessionStorage', createSessionStorageMock())
    vi.clearAllMocks()
    chatServiceMock.getSkills.mockResolvedValue(skills)
    chatServiceMock.getToolCatalog.mockResolvedValue(toolCatalog)
    chatServiceMock.getKnowledgeBases.mockResolvedValue(knowledgeBases)
    chatServiceMock.planAgentTask.mockResolvedValue(plannedTask)
    chatServiceMock.approveAgentTaskPlan.mockResolvedValue(createTask({ status: 'PlanApproved', canRun: true }))
    chatServiceMock.decideAgentApproval.mockResolvedValue(createPlanApproval({ status: 'Approved' }))
    chatServiceMock.runAgentTask.mockResolvedValue(createTask({
      status: 'Running',
      canRun: false,
      isRunInProgress: true
    }))
    chatServiceMock.retryAgentTask.mockResolvedValue(createTask({
      status: 'Running',
      canRetry: false,
      isRunInProgress: true
    }))
    chatServiceMock.getAgentTasksBySession.mockResolvedValue([plannedTask])
    chatServiceMock.getAgentTaskApprovals.mockResolvedValue([])
    chatServiceMock.getAgentTaskAuditSummary.mockResolvedValue([])
    chatServiceMock.downloadArtifact.mockResolvedValue(new Blob(['report']))
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

  it('sends selected plugin tools as a planner preference without leaving auto skill mode', async () => {
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-1')
    const store = useChatStore()

    await store.loadSkills()
    await store.loadPluginTools()
    store.togglePluginTool('rag_search')

    await store.planAgentTask('查资料并生成报告')

    expect(chatServiceMock.getToolCatalog).toHaveBeenCalledWith(null)
    expect(chatServiceMock.planAgentTask).toHaveBeenCalledWith(expect.objectContaining({
      skillCode: null,
      preferredToolCodes: ['rag_search']
    }))
  })

  it('approves a pending plan approval before running the task', async () => {
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-1')
    const store = useChatStore()
    store.agentApprovals = [createPlanApproval()]

    await store.approveAndRunAgentTask('task-1')

    expect(chatServiceMock.decideAgentApproval).toHaveBeenCalledWith(
      'approval-1',
      'approve',
      'Approved from primary plan CTA'
    )
    expect(chatServiceMock.approveAgentTaskPlan).not.toHaveBeenCalled()
    expect(chatServiceMock.runAgentTask).toHaveBeenCalledWith('task-1')
  })

  it('keeps the approved plan state visible when run fails after approval', async () => {
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-1')
    const approvedTask = createTask({
      status: 'PlanApproved',
      canRun: true
    })
    const store = useChatStore()
    chatServiceMock.getAgentTasksBySession.mockResolvedValue([approvedTask])
    chatServiceMock.runAgentTask.mockRejectedValue(new ApiError('API Error: 400', 400, {
      code: 'agent_worker_unavailable',
      detail: '没有可用 DataWorker，任务尚未入队。'
    }))

    await store.approveAndRunAgentTask('task-1')

    expect(chatServiceMock.approveAgentTaskPlan).toHaveBeenCalledWith('task-1')
    expect(store.latestAgentTask?.status).toBe('PlanApproved')
    expect(store.agentErrorMessage).toBe('没有可用 DataWorker，任务尚未入队。')
  })

  it('uses the retry endpoint for failed tasks', async () => {
    const store = useChatStore()

    await store.retryAgentTask('task-1')

    expect(chatServiceMock.retryAgentTask).toHaveBeenCalledWith('task-1')
    expect(chatServiceMock.runAgentTask).not.toHaveBeenCalled()
  })

  it('downloads artifacts only through the backend supplied download URL', async () => {
    const click = vi.fn()
    const anchor = {
      href: '',
      download: '',
      click
    }
    const createObjectURL = vi.fn(() => 'blob:artifact')
    const revokeObjectURL = vi.fn()
    vi.stubGlobal('document', {
      createElement: vi.fn(() => anchor)
    })
    vi.stubGlobal('URL', {
      createObjectURL,
      revokeObjectURL
    })
    const store = useChatStore()

    await store.downloadArtifact({
      id: 'artifact-1',
      taskId: 'task-1',
      name: 'report.pdf',
      type: 'Report',
      status: 'Draft',
      relativePath: 'draft/report.pdf',
      fileSize: 1024,
      mimeType: 'application/pdf',
      version: 1,
      updatedAt: '2026-06-22T07:00:00Z',
      previewKind: 'pdf',
      downloadUrl: '/api/aigateway/artifact/artifact-1/download'
    })

    expect(chatServiceMock.downloadArtifact).toHaveBeenCalledWith('/api/aigateway/artifact/artifact-1/download')
    expect(anchor.href).toBe('blob:artifact')
    expect(anchor.download).toBe('report.pdf')
    expect(click).toHaveBeenCalledOnce()
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:artifact')
  })

  it('does not fabricate an artifact download path when the backend omits it', async () => {
    const store = useChatStore()

    await store.downloadArtifact({
      id: 'artifact-1',
      taskId: 'task-1',
      name: 'report.pdf',
      type: 'Report',
      status: 'Draft',
      relativePath: 'draft/report.pdf',
      fileSize: 1024,
      mimeType: 'application/pdf',
      version: 1,
      updatedAt: '2026-06-22T07:00:00Z',
      previewKind: 'pdf',
      downloadUrl: ''
    })

    expect(chatServiceMock.downloadArtifact).not.toHaveBeenCalled()
    expect(store.agentErrorMessage).toContain('后端未返回产物下载地址')
  })
})
