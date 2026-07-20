import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '@/services/apiClient'
import { useAgentWorkbench } from '@/composables/useAgentWorkbench'
import { useChatStore } from '@/stores/chatStore'
import { useApprovalStore } from '@/stores/approvalStore'
import { useAgentTaskStore } from '@/stores/agentTaskStore'
import { useMessageStore } from '@/stores/messageStore'
import { useSessionStore } from '@/stores/sessionStore'
import { ChunkType, MessageRole } from '@/types/protocols'

const chatServiceMock = vi.hoisted(() => ({
  getSkills: vi.fn(),
  getSessions: vi.fn(),
  getHistory: vi.fn(),
  getPendingApprovals: vi.fn(),
  getToolCatalog: vi.fn(),
  getKnowledgeBases: vi.fn(),
  planAgentTaskStream: vi.fn(),
  approveAgentTaskPlan: vi.fn(),
  decideAgentApproval: vi.fn(),
  runAgentTask: vi.fn(),
  retryAgentTask: vi.fn(),
  submitFinalReview: vi.fn(),
  finalizeWorkspace: vi.fn(),
  getAgentTasksBySession: vi.fn(),
  getAgentTaskApprovals: vi.fn(),
  getAgentTaskAuditSummary: vi.fn(),
  getTimeline: vi.fn(),
  getWorkspace: vi.fn(),
  getArtifactPreview: vi.fn(),
  uploadFile: vi.fn(),
  downloadArtifact: vi.fn(),
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

function createDeferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((complete) => {
    resolve = complete
  })

  return { promise, resolve }
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
    updatedAt: '2026-06-22T07:00:00Z',
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
    updatedAt: '2026-06-22T07:00:00Z',
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
    updatedAt: '2026-06-22T07:00:00Z',
  },
]

const knowledgeBases = [
  {
    id: 'kb-1',
    name: '报警手册',
    description: '报警处理知识库',
    embeddingModelId: 'embedding-1',
    documentCount: 2,
  },
  {
    id: 'kb-2',
    name: '设备手册',
    description: '设备维护知识库',
    embeddingModelId: 'embedding-1',
    documentCount: 3,
  },
]

const toolCatalog = {
  version: 1,
  availableToolCount: 2,
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
      isMock: false,
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
      isMock: false,
    },
  ],
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
  isRunQueued: false,
}

function createTask(overrides: Partial<typeof plannedTask> = {}) {
  return {
    ...plannedTask,
    ...overrides,
  }
}

function createWorkspace(taskId = 'task-1', workspaceCode = 'WS-1') {
  return {
    id: `workspace-${taskId}`,
    workspaceCode,
    taskId,
    status: 'Draft',
    files: [],
    artifacts: [],
  }
}

function bindArtifactOwnership(store: ReturnType<typeof useChatStore>, artifactId = 'artifact-1') {
  store.agentTasks = [createTask({ workspaceCode: 'WS-1' })]
  store.currentWorkspace = {
    id: 'workspace-1',
    workspaceCode: 'WS-1',
    taskId: 'task-1',
    status: 'Draft',
    files: [],
    artifacts: [
      {
        id: artifactId,
        name: 'report.pdf',
        downloadUrl: `/api/aigateway/artifact/${artifactId}/download`,
      },
    ],
  } as never
}

function mockPlanAgentTaskStream(task = plannedTask) {
  chatServiceMock.getAgentTasksBySession.mockResolvedValue([task])
  chatServiceMock.planAgentTaskStream.mockImplementation(async (_payload, callbacks) => {
    callbacks.onChunkReceived({
      source: 'PlanAgentTaskStreamHandler',
      type: ChunkType.Text,
      content: '正在理解目标并识别可用能力...\n',
    })
    callbacks.onChunkReceived({
      source: 'PlanAgentTaskStreamHandler',
      type: ChunkType.Text,
      content: `我已生成计划草案：${task.title}\n`,
    })
    callbacks.onChunkReceived({
      source: 'PlanAgentTaskStreamHandler',
      type: ChunkType.AgentTask,
      content: JSON.stringify(task),
    })
    callbacks.onComplete()
  })
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
    ...overrides,
  }
}

describe('chatStore skills', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.stubGlobal('sessionStorage', createSessionStorageMock())
    vi.clearAllMocks()
    chatServiceMock.getSkills.mockResolvedValue(skills)
    chatServiceMock.getSessions.mockResolvedValue([])
    chatServiceMock.getHistory.mockResolvedValue({
      items: [],
      beforeSequence: null,
      afterSequence: null,
      hasMoreBefore: false,
      hasMoreAfter: false,
    })
    chatServiceMock.getPendingApprovals.mockResolvedValue([])
    chatServiceMock.getToolCatalog.mockResolvedValue(toolCatalog)
    chatServiceMock.getKnowledgeBases.mockResolvedValue(knowledgeBases)
    mockPlanAgentTaskStream(plannedTask)
    chatServiceMock.approveAgentTaskPlan.mockResolvedValue(
      createTask({ status: 'PlanApproved', canRun: true }),
    )
    chatServiceMock.decideAgentApproval.mockResolvedValue(
      createPlanApproval({ status: 'Approved' }),
    )
    chatServiceMock.runAgentTask.mockResolvedValue(
      createTask({
        status: 'Running',
        canRun: false,
        isRunInProgress: true,
      }),
    )
    chatServiceMock.retryAgentTask.mockResolvedValue(
      createTask({
        status: 'Running',
        canRetry: false,
        isRunInProgress: true,
      }),
    )
    chatServiceMock.getAgentTasksBySession.mockResolvedValue([plannedTask])
    chatServiceMock.getAgentTaskApprovals.mockResolvedValue([])
    chatServiceMock.getAgentTaskAuditSummary.mockResolvedValue([])
    chatServiceMock.getWorkspace.mockResolvedValue(createWorkspace())
    chatServiceMock.getArtifactPreview.mockResolvedValue(null)
    chatServiceMock.uploadFile.mockResolvedValue({ id: 'upload-1', name: 'input.txt' })
    chatServiceMock.downloadArtifact.mockResolvedValue(new Blob(['report']))
    chatServiceMock.getTimeline.mockResolvedValue({
      items: [],
      beforeSequence: null,
      afterSequence: null,
      hasMore: false,
      hasMoreBefore: false,
      hasMoreAfter: false,
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
    await store.selectSkill('missing_skill')

    expect(store.selectedSkillCode).toBeNull()
    expect(store.selectedSkill).toBeNull()

    await store.selectSkill(null)

    expect(store.selectedSkillCode).toBeNull()
    expect(store.isSkillAutoMode).toBe(true)
  })

  it('keeps only the latest tool catalog when skill requests complete out of order', async () => {
    activateResolvedSession()
    const firstCatalog = createDeferred<typeof toolCatalog>()
    const secondCatalog = createDeferred<typeof toolCatalog>()
    chatServiceMock.getToolCatalog.mockImplementation((skillCode) =>
      skillCode === 'general_report' ? firstCatalog.promise : secondCatalog.promise,
    )
    const store = useChatStore()
    await store.loadSkills()

    const firstSelection = store.selectSkill('general_report')
    const secondSelection = store.selectSkill('knowledge_research')

    secondCatalog.resolve({
      ...toolCatalog,
      tools: [toolCatalog.tools[0]!],
    })
    await secondSelection
    firstCatalog.resolve({
      ...toolCatalog,
      tools: [toolCatalog.tools[1]!],
    })
    await firstSelection

    expect(store.selectedSkillCode).toBe('knowledge_research')
    expect(store.availablePluginTools.map((tool) => tool.toolCode)).toEqual(['rag_search'])
    expect(store.isSessionOperationInFlight).toBe(false)
    expect(store.isSessionTransitionBlocked).toBe(false)
  })

  it('restores the previous session runtime when cross-session activation fails', async () => {
    activateResolvedSession()
    const sessionStore = useSessionStore()
    sessionStore.upsertSession({ id: 'session-2', title: '第二会话' })
    const store = useChatStore()
    await store.loadSkills()
    await store.selectSkill('knowledge_research')
    const task = createTask({ sessionId: 'session-1', title: 'A 会话任务' })
    store.agentTasks = [task]
    chatServiceMock.getHistory.mockRejectedValueOnce(new Error('history unavailable'))

    const switched = await store.selectSession('session-2')

    expect(switched).toBe(false)
    expect(store.currentSessionId).toBe('session-1')
    expect(store.resolvedSessionId).toBe('session-1')
    expect(store.agentTasks).toEqual([task])
    expect(store.selectedSkillCode).toBe('knowledge_research')
    expect(store.availablePluginTools).toEqual(toolCatalog.tools)
    expect(store.isSessionTransitionBlocked).toBe(false)
    expect(store.errorMessage).toBe('请求失败，请稍后重试。')
  })

  it('reloads the auto tool catalog after a committed cross-session switch', async () => {
    activateResolvedSession()
    const sessionStore = useSessionStore()
    sessionStore.upsertSession({ id: 'session-2', title: '第二会话' })
    chatServiceMock.getToolCatalog.mockImplementation((skillCode) =>
      Promise.resolve({
        ...toolCatalog,
        tools: skillCode === 'general_report' ? [toolCatalog.tools[1]!] : [toolCatalog.tools[0]!],
      }),
    )
    chatServiceMock.getAgentTasksBySession.mockResolvedValue([])
    const store = useChatStore()
    await store.loadSkills()
    await store.selectSkill('general_report')
    expect(store.availablePluginTools.map((tool) => tool.toolCode)).toEqual([
      'generate_markdown_report',
    ])

    const switched = await store.selectSession('session-2')

    expect(switched).toBe(true)
    expect(store.resolvedSessionId).toBe('session-2')
    expect(store.selectedSkillCode).toBeNull()
    expect(store.selectedToolCodes).toEqual([])
    expect(store.availablePluginTools.map((tool) => tool.toolCode)).toEqual(['rag_search'])
    expect(chatServiceMock.getToolCatalog).toHaveBeenLastCalledWith(null)
  })

  it('captures runtime before synchronous initialization revokes action authority', async () => {
    activateResolvedSession()
    const store = useChatStore()
    await store.loadSkills()
    await store.selectSkill('knowledge_research')
    const task = createTask({ sessionId: 'session-1', title: '重挂载前任务' })
    store.agentTasks = [task]
    chatServiceMock.getSessions.mockRejectedValueOnce(new Error('session list unavailable'))

    store.prepareInitialization()

    expect(store.resolvedSessionId).toBeNull()
    expect(store.agentTasks).toEqual([])
    await expect(store.initialize()).rejects.toThrow('session list unavailable')

    expect(store.resolvedSessionId).toBe('session-1')
    expect(store.agentTasks).toEqual([task])
    expect(store.selectedSkillCode).toBe('knowledge_research')
    expect(store.availablePluginTools).toEqual(toolCatalog.tools)
    expect(store.isSessionTransitionBlocked).toBe(false)
  })

  it('invalidates hydration-time catalog requests before restoring the prior snapshot', async () => {
    activateResolvedSession()
    const store = useChatStore()
    await store.loadSkills()
    chatServiceMock.getToolCatalog.mockResolvedValueOnce({
      ...toolCatalog,
      tools: [toolCatalog.tools[1]!],
    })
    await store.selectSkill('general_report')
    const delayedCatalog = createDeferred<typeof toolCatalog>()
    chatServiceMock.getToolCatalog.mockImplementation((skillCode) =>
      skillCode === 'knowledge_research' ? delayedCatalog.promise : Promise.resolve(toolCatalog),
    )
    chatServiceMock.getSessions.mockRejectedValueOnce(new Error('session list unavailable'))

    store.prepareInitialization()
    const selection = store.selectSkill('knowledge_research')
    await expect(store.initialize()).rejects.toThrow('session list unavailable')

    expect(store.selectedSkillCode).toBe('general_report')
    expect(store.availablePluginTools.map((tool) => tool.toolCode)).toEqual([
      'generate_markdown_report',
    ])
    delayedCatalog.resolve({
      ...toolCatalog,
      tools: [toolCatalog.tools[0]!],
    })
    await selection

    expect(store.selectedSkillCode).toBe('general_report')
    expect(store.availablePluginTools.map((tool) => tool.toolCode)).toEqual([
      'generate_markdown_report',
    ])
    expect(store.isSessionOperationInFlight).toBe(false)
  })

  it('rolls back cross-session activation when the task projection cannot be loaded', async () => {
    activateResolvedSession()
    const sessionStore = useSessionStore()
    sessionStore.upsertSession({ id: 'session-2', title: '第二会话' })
    const task = createTask({ sessionId: 'session-1', title: 'A 会话任务' })
    const store = useChatStore()
    store.agentTasks = [task]
    chatServiceMock.getAgentTasksBySession.mockRejectedValueOnce(
      new Error('task projection unavailable'),
    )

    const switched = await store.selectSession('session-2')

    expect(switched).toBe(false)
    expect(store.resolvedSessionId).toBe('session-1')
    expect(store.agentTasks).toEqual([task])
    expect(store.errorMessage).toBe('请求失败，请稍后重试。')
  })

  it('preserves the last trusted task projection when active-session polling fails', async () => {
    activateResolvedSession()
    const task = createTask({ sessionId: 'session-1', title: '已验证任务' })
    const store = useChatStore()
    store.agentTasks = [task]
    chatServiceMock.getAgentTasksBySession.mockRejectedValueOnce(
      new Error('task projection unavailable'),
    )

    await store.refreshAgentTaskSnapshot('task-1')

    expect(store.agentTasks).toEqual([task])
    expect(store.errorMessage).toBe('加载任务状态失败：请求失败，请稍后重试。')
    expect(store.isSessionOperationInFlight).toBe(false)
  })

  it('rolls back cross-session activation when the workspace projection cannot be loaded', async () => {
    activateResolvedSession()
    const sessionStore = useSessionStore()
    sessionStore.upsertSession({ id: 'session-2', title: '第二会话' })
    const trustedTask = createTask({
      sessionId: 'session-1',
      title: 'A 会话任务',
      workspaceCode: 'WS-A',
    })
    const targetTask = createTask({
      id: 'task-2',
      taskCode: 'AGT-2',
      sessionId: 'session-2',
      title: 'B 会话任务',
      workspaceCode: 'WS-B',
    })
    const trustedWorkspace = createWorkspace('task-1', 'WS-A')
    const store = useChatStore()
    store.agentTasks = [trustedTask]
    store.currentWorkspace = trustedWorkspace
    chatServiceMock.getAgentTasksBySession.mockResolvedValueOnce([targetTask])
    chatServiceMock.getWorkspace.mockRejectedValueOnce(new Error('workspace unavailable'))

    const switched = await store.selectSession('session-2')

    expect(switched).toBe(false)
    expect(store.resolvedSessionId).toBe('session-1')
    expect(store.agentTasks).toEqual([trustedTask])
    expect(store.currentWorkspace).toEqual(trustedWorkspace)
    expect(store.errorMessage).toBe('请求失败，请稍后重试。')
  })

  it('keeps one trusted task-workspace generation when active polling loses the workspace', async () => {
    activateResolvedSession()
    const trustedTask = createTask({
      sessionId: 'session-1',
      title: '可信任务',
      workspaceCode: 'WS-A',
      canSubmitFinalReview: false,
    })
    const uncommittedTask = createTask({
      sessionId: 'session-1',
      title: '未完成投影任务',
      workspaceCode: 'WS-A',
      canSubmitFinalReview: true,
    })
    const trustedWorkspace = createWorkspace('task-1', 'WS-A')
    const store = useChatStore()
    store.agentTasks = [trustedTask]
    store.currentWorkspace = trustedWorkspace
    chatServiceMock.getAgentTasksBySession.mockResolvedValueOnce([uncommittedTask])
    chatServiceMock.getWorkspace.mockRejectedValueOnce(new Error('workspace unavailable'))

    await store.refreshAgentTaskSnapshot('task-1')

    expect(store.agentTasks).toEqual([trustedTask])
    expect(store.currentWorkspace).toEqual(trustedWorkspace)
    expect(store.latestAgentTask?.canSubmitFinalReview).toBe(false)
    expect(store.errorMessage).toBe('加载产物工作区失败：请求失败，请稍后重试。')
  })

  it('marks agent approval authority unknown when polling cannot refresh approvals', async () => {
    activateResolvedSession()
    const approval = createPlanApproval()
    const store = useChatStore()
    store.agentTasks = [plannedTask]
    store.agentApprovals = [approval]
    chatServiceMock.getAgentTasksBySession.mockResolvedValueOnce([plannedTask])
    chatServiceMock.getAgentTaskApprovals.mockRejectedValueOnce(
      new Error('approval projection unavailable'),
    )

    await store.refreshAgentTaskSnapshot('task-1')
    const decision = await store.decideAgentApproval(approval, 'approve')

    expect(decision).toBeNull()
    expect(store.isAgentApprovalAuthorityUnknown).toBe(true)
    expect(store.agentApprovals).toEqual([approval])
    expect(chatServiceMock.decideAgentApproval).not.toHaveBeenCalled()
  })

  it('does not enable final review without a workspace projection aligned to the latest task', () => {
    activateResolvedSession()
    const store = useChatStore()
    store.agentTasks = [
      createTask({
        workspaceCode: 'WS-A',
        canSubmitFinalReview: true,
        canApproveFinal: true,
      }),
    ]
    const workbench = useAgentWorkbench()

    expect(workbench.canSubmitFinalReview.value).toBe(false)
    expect(workbench.canFinalizeWorkspace.value).toBe(false)

    store.currentWorkspace = {
      ...createWorkspace('task-from-session-b', 'WS-A'),
      artifacts: [{ id: 'artifact-1' }],
    } as never
    expect(workbench.canSubmitFinalReview.value).toBe(false)

    store.currentWorkspace = {
      ...createWorkspace('task-1', 'WS-A'),
      artifacts: [{ id: 'artifact-1' }],
    } as never
    expect(workbench.canSubmitFinalReview.value).toBe(true)
    expect(workbench.canFinalizeWorkspace.value).toBe(true)
  })

  it('scopes an unknown agent approval projection to the latest task', () => {
    activateResolvedSession()
    const taskStore = useAgentTaskStore()
    const taskA = createTask({ id: 'task-a', taskCode: 'AGT-A', canRun: false })
    const taskB = createTask({ id: 'task-b', taskCode: 'AGT-B', canRun: true })
    taskStore.agentTasks = [taskA]
    taskStore.markApprovalAuthorityUnknown('task-a')
    const store = useChatStore()
    const workbench = useAgentWorkbench()

    expect(store.isAgentApprovalAuthorityUnknown).toBe(true)

    taskStore.agentTasks = [taskB, taskA]

    expect(store.isAgentApprovalAuthorityUnknown).toBe(false)
    expect(workbench.canRunTask.value).toBe(true)
  })

  it('publishes a visible error when cold initialization cannot load sessions', async () => {
    chatServiceMock.getSessions.mockRejectedValueOnce(new Error('session list unavailable'))
    const store = useChatStore()

    store.prepareInitialization()
    await expect(store.initialize()).rejects.toThrow('session list unavailable')

    expect(store.currentSessionId).toBeNull()
    expect(store.resolvedSessionId).toBeNull()
    expect(store.isSessionTransitionBlocked).toBe(false)
    expect(store.errorMessage).toBe('请求失败，请稍后重试。')
  })

  it('keeps cold-hydration catalog failures visible after session activation', async () => {
    chatServiceMock.getSessions.mockResolvedValueOnce([{ id: 'session-1', title: '测试会话' }])
    chatServiceMock.getSkills.mockRejectedValueOnce(new ApiError('API Error: 403', 403, null))
    chatServiceMock.getAgentTasksBySession.mockResolvedValue([])
    const store = useChatStore()

    store.prepareInitialization()
    await store.initialize()

    expect(store.resolvedSessionId).toBe('session-1')
    expect(store.availableSkills).toEqual([])
    expect(store.errorMessage).toBe('加载 Skill 列表失败：当前账号没有访问该功能的权限。')
  })

  it('does not let composer interaction erase a source-scoped hydration failure', async () => {
    const sessions = createDeferred<Array<{ id: string; title: string }>>()
    chatServiceMock.getSessions.mockReturnValueOnce(sessions.promise)
    chatServiceMock.getSkills.mockRejectedValueOnce(new ApiError('API Error: 403', 403, null))
    chatServiceMock.getAgentTasksBySession.mockResolvedValue([])
    const store = useChatStore()

    store.prepareInitialization()
    const initialization = store.initialize()
    await Promise.resolve()
    await Promise.resolve()
    store.clearCurrentSessionError()
    sessions.resolve([{ id: 'session-1', title: '测试会话' }])
    await initialization

    expect(store.resolvedSessionId).toBe('session-1')
    expect(store.errorMessage).toBe('加载 Skill 列表失败：当前账号没有访问该功能的权限。')
  })

  it('clears only a recovered catalog-selection error during hydration', async () => {
    const sessions = createDeferred<Array<{ id: string; title: string }>>()
    chatServiceMock.getSessions.mockReturnValueOnce(sessions.promise)
    const store = useChatStore()
    await store.loadSkills()
    chatServiceMock.getKnowledgeBases.mockRejectedValueOnce(
      new ApiError('API Error: 403', 403, null),
    )
    chatServiceMock.getToolCatalog
      .mockRejectedValueOnce(new Error('catalog unavailable'))
      .mockResolvedValueOnce(toolCatalog)

    store.prepareInitialization()
    const initialization = store.initialize()
    await Promise.resolve()
    await store.selectSkill('knowledge_research')
    expect(store.errorMessage).toContain('加载插件能力失败')

    await store.selectSkill('knowledge_research')
    sessions.resolve([{ id: 'session-1', title: '测试会话' }])
    await initialization

    expect(store.resolvedSessionId).toBe('session-1')
    expect(store.errorMessage).toBe('加载知识库列表失败：当前账号没有访问该功能的权限。')
  })

  it('preserves uploads when the same committed session is rehydrated', async () => {
    activateResolvedSession()
    const store = useChatStore()
    store.uploadedFiles = [{ id: 'upload-a', name: 'input-a.txt' }] as never
    chatServiceMock.getSessions.mockResolvedValueOnce([{ id: 'session-1', title: '测试会话' }])

    store.prepareInitialization()
    await store.initialize()

    expect(store.resolvedSessionId).toBe('session-1')
    expect(store.uploadedFiles).toEqual([{ id: 'upload-a', name: 'input-a.txt' }])
  })

  it('clears composer selections and uploads when rehydration falls back to another session', async () => {
    activateResolvedSession()
    const store = useChatStore()
    await store.loadSkills()
    await store.loadKnowledgeBases()
    await store.selectSkill('knowledge_research')
    store.togglePluginTool('rag_search')
    store.selectKnowledgeBase('kb-1')
    store.uploadedFiles = [{ id: 'upload-a', name: 'input-a.txt' }] as never
    chatServiceMock.getSessions.mockResolvedValueOnce([{ id: 'session-2', title: '第二会话' }])
    chatServiceMock.getAgentTasksBySession.mockResolvedValue([])

    store.prepareInitialization()
    await store.initialize()

    expect(store.resolvedSessionId).toBe('session-2')
    expect(store.selectedSkillCode).toBeNull()
    expect(store.selectedToolCodes).toEqual([])
    expect(store.selectedKnowledgeBaseId).toBeNull()
    expect(store.uploadedFiles).toEqual([])
  })

  it('does not plan against an unresolved persisted session id', async () => {
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('missing-session')
    const store = useChatStore()

    const task = await store.planAgentTask('这条计划不能发给陈旧会话')

    expect(task).toBeNull()
    expect(chatServiceMock.planAgentTaskStream).not.toHaveBeenCalled()
    expect(store.currentMessages).toEqual([])
  })

  it('does not plan while a function approval is pending', async () => {
    activateResolvedSession()
    const request = {
      callId: 'call-pending',
      name: 'controlled tool',
      targetType: 'McpServer',
      targetName: 'cloud-read',
      toolName: 'queryDeviceLogs',
      args: {},
      requiresOnsiteAttestation: false,
    }
    useMessageStore().addMessage('session-1', {
      sessionId: 'session-1',
      role: MessageRole.Assistant,
      chunks: [
        {
          source: 'FinalAgentRunExecutor',
          type: ChunkType.ApprovalRequest,
          content: JSON.stringify(request),
          request,
          status: 'pending',
        } as never,
      ],
      isStreaming: false,
      timestamp: Date.now(),
    })
    useApprovalStore().sync('session-1')
    const store = useChatStore()

    const task = await store.planAgentTask('待审批时不能生成新计划')

    expect(task).toBeNull()
    expect(chatServiceMock.planAgentTaskStream).not.toHaveBeenCalled()
    expect(store.currentMessages).toHaveLength(1)
  })

  it('does not plan while function approval authority is unknown', async () => {
    activateResolvedSession()
    const approvalStore = useApprovalStore()
    approvalStore.sync('session-1')
    approvalStore.markAuthorityUnknown('session-1')
    const store = useChatStore()

    const task = await store.planAgentTask('审批投影未知时不能生成新计划')

    expect(task).toBeNull()
    expect(chatServiceMock.planAgentTaskStream).not.toHaveBeenCalled()
    expect(store.currentMessages).toEqual([])
  })

  it('does not upload against an unresolved persisted session id', async () => {
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('missing-session')
    const store = useChatStore()

    const uploaded = await store.uploadSessionFile(new Blob(['input']) as File)

    expect(uploaded).toBeNull()
    expect(chatServiceMock.uploadFile).not.toHaveBeenCalled()
  })

  it('blocks session transitions and planning while an upload is in flight', async () => {
    activateResolvedSession()
    const sessionStore = useSessionStore()
    sessionStore.upsertSession({ id: 'session-2', title: '第二会话' })
    const upload = createDeferred<{ id: string; name: string }>()
    chatServiceMock.uploadFile.mockReturnValue(upload.promise)
    const store = useChatStore()

    const uploadPromise = store.uploadSessionFile(new Blob(['input']) as File)

    expect(store.isSessionOperationInFlight).toBe(true)
    expect(store.isSessionTransitionBlocked).toBe(true)
    await store.selectSession('session-2')
    const plan = await store.planAgentTask('上传完成前不能跨会话生成计划')

    expect(store.currentSessionId).toBe('session-1')
    expect(store.resolvedSessionId).toBe('session-1')
    expect(plan).toBeNull()
    expect(chatServiceMock.planAgentTaskStream).not.toHaveBeenCalled()

    upload.resolve({ id: 'upload-1', name: 'input.txt' })
    await uploadPromise

    expect(store.isSessionOperationInFlight).toBe(false)
    expect(store.isSessionTransitionBlocked).toBe(false)
    expect(store.uploadedFiles).toEqual([{ id: 'upload-1', name: 'input.txt' }])
  })

  it('blocks session transitions while an agent task mutation is in flight', async () => {
    activateResolvedSession()
    const sessionStore = useSessionStore()
    sessionStore.upsertSession({ id: 'session-2', title: '第二会话' })
    const taskRun = createDeferred<ReturnType<typeof createTask>>()
    chatServiceMock.runAgentTask.mockReturnValue(taskRun.promise)
    const store = useChatStore()
    store.agentTasks = [plannedTask]

    const runPromise = store.runAgentTask('task-1')

    expect(store.isAgentBusy).toBe(true)
    expect(store.isSessionTransitionBlocked).toBe(true)
    await store.selectSession('session-2')

    expect(store.currentSessionId).toBe('session-1')
    expect(store.resolvedSessionId).toBe('session-1')

    taskRun.resolve(createTask({ status: 'Running', isRunInProgress: true }))
    await runPromise

    expect(store.isAgentBusy).toBe(false)
    expect(store.isSessionTransitionBlocked).toBe(false)
    expect(store.latestAgentTask?.sessionId).toBe('session-1')
  })

  it('omits retired skill and tool fields in auto mode before calling the Plan v2 stream', async () => {
    activateResolvedSession()
    const store = useChatStore()
    await store.loadSkills()

    await store.planAgentTask('查看 Cloud 设备日志')

    expect(chatServiceMock.planAgentTaskStream).toHaveBeenCalledWith(
      expect.objectContaining({
        sessionId: 'session-1',
        goal: '查看 Cloud 设备日志',
        pluginSelectionMode: 'BuiltInOnly',
        selectedPluginIds: [],
        capabilitySelectionMode: 'InferredFromGoal',
        requestedCapabilityCodes: [],
      }),
      expect.any(Object),
    )
    const payload = chatServiceMock.planAgentTaskStream.mock.calls.at(-1)?.[0]
    expect(payload).not.toHaveProperty('skillCode')
    expect(payload).not.toHaveProperty('preferredToolCodes')
  })

  it('blocks Plan v2 before HTTP when a legacy skill remains selected', async () => {
    activateResolvedSession()
    const store = useChatStore()
    await store.loadSkills()
    await store.selectSkill('knowledge_research')

    const planned = await store.planAgentTask('查手册')

    expect(planned).toBeNull()
    expect(chatServiceMock.planAgentTaskStream).not.toHaveBeenCalled()
    expect(store.errorMessage).toBe(
      '当前计划入口不再接受 Skill 或工具选择，请清除旧选择后重试。',
    )
    expect(store.currentMessages).toEqual([])
  })

  it('adds plan mode goal and PlanDraft reply to the conversation flow', async () => {
    activateResolvedSession()
    const store = useChatStore()
    const streamedTask = createTask({
      title: '流式临时设备日志计划',
      goal: '查看 DEV-001 最近 24 小时日志',
      status: 'Planning',
      planJson: JSON.stringify({
        planKind: 'PlanDraft',
        isExecutable: false,
        skillName: '流式临时计划',
        capabilityGaps: [],
      }),
      steps: [],
    })
    const canonicalTask = createTask({
      title: '设备日志分析',
      goal: '查看 DEV-001 最近 24 小时日志',
      status: 'WaitingPlanApproval',
      planJson: JSON.stringify({
        planKind: 'PlanDraft',
        isExecutable: false,
        skillName: '设备日志分析',
        capabilityGaps: [
          'No enabled and authorized tools are currently available for this PlanDraft.',
        ],
      }),
      steps: [
        {
          id: 'step-1',
          stepIndex: 1,
          title: '读取设备日志',
          description: '只读查询最近 24 小时日志',
          stepType: 'Tool',
          status: 'Pending',
          toolCode: 'query_device_logs',
          requiresApproval: false,
          errorMessage: null,
        },
      ],
    })
    const approval = createPlanApproval()
    const audit = {
      id: 'audit-1',
      taskId: 'task-1',
      actionCode: 'PlanCreated',
      targetType: 'AgentTask',
      targetName: '设备日志分析',
      result: 'Succeeded',
      summary: '计划已生成',
      createdAt: '2026-06-22T07:01:00Z',
      metadata: {},
    }
    const timelineEvent = {
      sequence: 11,
      eventType: 'AgentTaskPlanCreated',
      createdAt: '2026-06-22T07:01:00Z',
      agentTaskId: 'task-1',
      agentTaskTitle: '设备日志分析',
      agentTaskStatus: 'WaitingPlanApproval',
    }
    mockPlanAgentTaskStream(streamedTask)
    chatServiceMock.getAgentTasksBySession.mockResolvedValue([canonicalTask])
    chatServiceMock.getAgentTaskApprovals.mockResolvedValue([approval])
    chatServiceMock.getAgentTaskAuditSummary.mockResolvedValue([audit])
    chatServiceMock.getTimeline.mockResolvedValue({
      items: [timelineEvent],
      beforeSequence: 11,
      afterSequence: 11,
      hasMore: false,
      hasMoreBefore: false,
      hasMoreAfter: false,
    })

    const plannedTask = await store.planAgentTask('查看 DEV-001 最近 24 小时日志')

    expect(store.currentMessages).toHaveLength(2)
    expect(store.currentMessages[0]?.role).toBe(MessageRole.User)
    expect(store.currentMessages[0]?.chunks[0]?.content).toBe('查看 DEV-001 最近 24 小时日志')
    expect(store.currentMessages[1]?.role).toBe(MessageRole.Assistant)
    expect(store.currentMessages[1]?.isStreaming).toBe(false)
    expect(store.currentMessages[1]?.chunks[0]?.content).toContain('我已生成计划草案')
    const projectionCallOrder = [
      chatServiceMock.planAgentTaskStream.mock.invocationCallOrder[0],
      chatServiceMock.getAgentTasksBySession.mock.invocationCallOrder[0],
      chatServiceMock.getAgentTaskApprovals.mock.invocationCallOrder[0],
      chatServiceMock.getAgentTaskAuditSummary.mock.invocationCallOrder[0],
      chatServiceMock.getTimeline.mock.invocationCallOrder[0],
    ]
    expect(projectionCallOrder).toEqual([...projectionCallOrder].sort((left, right) => left - right))
    expect(plannedTask?.title).toBe('设备日志分析')
    expect(store.latestAgentTask?.title).toBe('设备日志分析')
    expect(store.latestAgentTask?.planJson).toBe(canonicalTask.planJson)
    expect(store.latestAgentTask?.steps).toEqual(canonicalTask.steps)
    expect(store.agentApprovals).toEqual([approval])
    expect(store.agentAuditSummary).toEqual([audit])
    expect(store.timelineEvents).toEqual([timelineEvent])
  })

  it('blocks Plan v2 before HTTP when a knowledge skill selection remains active', async () => {
    activateResolvedSession()
    const store = useChatStore()

    await store.loadSkills()
    await store.loadKnowledgeBases()
    await store.selectSkill('knowledge_research')
    store.selectKnowledgeBase('kb-2')

    await store.planAgentTask('查设备手册')

    expect(chatServiceMock.planAgentTaskStream).not.toHaveBeenCalled()
    expect(store.errorMessage).toBe(
      '当前计划入口不再接受 Skill 或工具选择，请清除旧选择后重试。',
    )

    await store.selectSkill('data_analysis')

    await store.planAgentTask('分析产能')

    expect(chatServiceMock.planAgentTaskStream).not.toHaveBeenCalled()
    expect(store.currentMessages).toEqual([])
  })

  it('blocks Plan v2 before HTTP when a legacy tool remains selected', async () => {
    activateResolvedSession()
    const store = useChatStore()

    await store.loadSkills()
    await store.loadPluginTools()
    store.togglePluginTool('rag_search')

    const planned = await store.planAgentTask('查资料并生成报告')

    expect(chatServiceMock.getToolCatalog).toHaveBeenCalledWith(null)
    expect(planned).toBeNull()
    expect(chatServiceMock.planAgentTaskStream).not.toHaveBeenCalled()
    expect(store.errorMessage).toBe(
      '当前计划入口不再接受 Skill 或工具选择，请清除旧选择后重试。',
    )
    expect(store.currentMessages).toEqual([])
  })

  it('treats a tool catalog response without tools as an empty catalog', async () => {
    const store = useChatStore()

    await store.loadPluginTools()
    store.togglePluginTool('rag_search')
    chatServiceMock.getToolCatalog.mockResolvedValueOnce({
      version: 2,
      availableToolCount: 0,
      riskSummary: {},
    })

    await expect(store.loadPluginTools()).resolves.toBeUndefined()

    expect(store.availablePluginTools).toEqual([])
    expect(store.selectedToolCodes).toEqual([])
  })

  it('approves a pending plan approval before running the task', async () => {
    activateResolvedSession()
    const store = useChatStore()
    store.agentTasks = [plannedTask]
    store.agentApprovals = [createPlanApproval()]

    await store.approveAndRunAgentTask('task-1')

    expect(chatServiceMock.decideAgentApproval).toHaveBeenCalledWith(
      'approval-1',
      'approve',
      'Approved from primary plan CTA',
    )
    expect(chatServiceMock.approveAgentTaskPlan).not.toHaveBeenCalled()
    expect(chatServiceMock.runAgentTask).toHaveBeenCalledWith('task-1')
  })

  it('keeps the approved plan state visible when run fails after approval', async () => {
    activateResolvedSession()
    const approvedTask = createTask({
      status: 'PlanApproved',
      canRun: true,
    })
    const store = useChatStore()
    store.agentTasks = [plannedTask]
    chatServiceMock.getAgentTasksBySession.mockResolvedValue([approvedTask])
    chatServiceMock.runAgentTask.mockRejectedValue(
      new ApiError('API Error: 400', 400, {
        code: 'agent_worker_unavailable',
        detail: '没有可用 DataWorker，任务尚未入队。',
      }),
    )

    await store.approveAndRunAgentTask('task-1')

    expect(chatServiceMock.approveAgentTaskPlan).toHaveBeenCalledWith('task-1')
    expect(store.latestAgentTask?.status).toBe('PlanApproved')
    expect(store.errorMessage).toBe('当前没有可用 DataWorker，请检查 Worker 状态。')
  })

  it('blocks a repeated agent approval when its committed outcome cannot be reconciled', async () => {
    activateResolvedSession()
    const approval = createPlanApproval()
    const store = useChatStore()
    store.agentTasks = [plannedTask]
    store.agentApprovals = [approval]
    chatServiceMock.decideAgentApproval.mockResolvedValue(
      createPlanApproval({ status: 'Approved' }),
    )
    chatServiceMock.getAgentTaskApprovals
      .mockRejectedValueOnce(new Error('approval projection unavailable'))
      .mockRejectedValueOnce(new Error('approval projection unavailable'))
    chatServiceMock.getAgentTasksBySession.mockResolvedValue([plannedTask])

    const first = await store.decideAgentApproval(approval, 'approve', 'ok')
    const second = await store.decideAgentApproval(approval, 'approve', 'retry')

    expect(first).toBeNull()
    expect(second).toBeNull()
    expect(chatServiceMock.decideAgentApproval).toHaveBeenCalledOnce()
    expect(store.isAgentApprovalAuthorityUnknown).toBe(true)
    expect(store.agentApprovals).toEqual([approval])
  })

  it('does not repeat plan approval after approval ACK and failed task reconciliation', async () => {
    activateResolvedSession()
    const approval = createPlanApproval()
    const store = useChatStore()
    store.agentTasks = [plannedTask]
    store.agentApprovals = [approval]
    chatServiceMock.decideAgentApproval.mockResolvedValue(
      createPlanApproval({ status: 'Approved' }),
    )
    chatServiceMock.runAgentTask.mockRejectedValue(new Error('run response unavailable'))
    chatServiceMock.getAgentTasksBySession.mockRejectedValue(
      new Error('task projection unavailable'),
    )

    await store.approveAndRunAgentTask('task-1')
    await store.approveAndRunAgentTask('task-1')

    expect(chatServiceMock.decideAgentApproval).toHaveBeenCalledOnce()
    expect(chatServiceMock.runAgentTask).toHaveBeenCalledOnce()
    expect(store.isAgentApprovalAuthorityUnknown).toBe(true)
  })

  it('uses the retry endpoint for failed tasks', async () => {
    activateResolvedSession()
    const store = useChatStore()
    store.agentTasks = [plannedTask]

    await store.retryAgentTask('task-1')

    expect(chatServiceMock.retryAgentTask).toHaveBeenCalledWith('task-1')
    expect(chatServiceMock.runAgentTask).not.toHaveBeenCalled()
  })

  it('blocks stale task, approval, workspace, and artifact identifiers from another session', async () => {
    activateResolvedSession()
    const store = useChatStore()
    store.agentTasks = [plannedTask]
    bindArtifactOwnership(store)

    const taskResult = await store.runAgentTask('task-from-session-b')
    const previewResult = await store.loadArtifactPreview('artifact-from-session-b')
    const approvalResult = await store.decideAgentApproval(
      {
        id: 'approval-from-session-b',
        taskId: 'task-from-session-b',
        status: 'Pending',
      } as never,
      'approve',
    )
    const submitResult = await store.submitFinalReview('WS-FROM-SESSION-B')
    const finalizeResult = await store.finalizeWorkspace('WS-FROM-SESSION-B')
    await store.downloadArtifact({
      id: 'artifact-from-session-b',
      name: 'foreign.pdf',
      type: 'Report',
      status: 'Draft',
      relativePath: 'draft/foreign.pdf',
      fileSize: 1,
      mimeType: 'application/pdf',
      version: 1,
      updatedAt: '2026-06-22T07:00:00Z',
      previewKind: 'pdf',
      downloadUrl: '/api/aigateway/artifact/foreign/download',
    })

    expect(taskResult).toBeNull()
    expect(previewResult).toBeNull()
    expect(approvalResult).toBeNull()
    expect(submitResult).toBeNull()
    expect(finalizeResult).toBeNull()
    expect(chatServiceMock.runAgentTask).not.toHaveBeenCalled()
    expect(chatServiceMock.getArtifactPreview).not.toHaveBeenCalled()
    expect(chatServiceMock.decideAgentApproval).not.toHaveBeenCalled()
    expect(chatServiceMock.submitFinalReview).not.toHaveBeenCalled()
    expect(chatServiceMock.finalizeWorkspace).not.toHaveBeenCalled()
    expect(chatServiceMock.downloadArtifact).not.toHaveBeenCalled()
    expect(store.errorMessage).toBe('操作目标不属于当前会话，已阻止请求。')
  })

  it('requires both caller and canonical agent approval state to remain pending', async () => {
    activateResolvedSession()
    const store = useChatStore()
    store.agentTasks = [plannedTask]
    store.agentApprovals = [createPlanApproval()]

    const result = await store.decideAgentApproval(
      createPlanApproval({ status: 'Approved' }),
      'approve',
    )

    expect(result).toBeNull()
    expect(chatServiceMock.decideAgentApproval).not.toHaveBeenCalled()
    expect(store.errorMessage).toBe('操作目标不属于当前会话，已阻止请求。')
  })

  it('downloads artifacts only through the backend supplied download URL', async () => {
    activateResolvedSession()
    const click = vi.fn()
    const anchor = {
      href: '',
      download: '',
      click,
    }
    const createObjectURL = vi.fn(() => 'blob:artifact')
    const revokeObjectURL = vi.fn()
    vi.stubGlobal('document', {
      createElement: vi.fn(() => anchor),
    })
    vi.stubGlobal('URL', {
      createObjectURL,
      revokeObjectURL,
    })
    const store = useChatStore()
    bindArtifactOwnership(store)

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
      downloadUrl: '/api/aigateway/artifact/artifact-1/download',
    })

    expect(chatServiceMock.downloadArtifact).toHaveBeenCalledWith(
      '/api/aigateway/artifact/artifact-1/download',
    )
    expect(anchor.href).toBe('blob:artifact')
    expect(anchor.download).toBe('report.pdf')
    expect(click).toHaveBeenCalledOnce()
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:artifact')
  })

  it('resolves artifact metadata from the trusted workspace instead of caller input', async () => {
    activateResolvedSession()
    const click = vi.fn()
    const anchor = { href: '', download: '', click }
    vi.stubGlobal('document', { createElement: vi.fn(() => anchor) })
    vi.stubGlobal('URL', {
      createObjectURL: vi.fn(() => 'blob:artifact'),
      revokeObjectURL: vi.fn(),
    })
    const store = useChatStore()
    bindArtifactOwnership(store)
    store.currentWorkspace!.artifacts[0] = {
      id: 'artifact-1',
      name: 'trusted.pdf',
      downloadUrl: '/api/aigateway/artifact/artifact-1/download',
    } as never

    await store.downloadArtifact({
      id: 'artifact-1',
      name: 'tampered.exe',
      downloadUrl: 'https://attacker.invalid/payload',
    } as never)

    expect(chatServiceMock.downloadArtifact).toHaveBeenCalledWith(
      '/api/aigateway/artifact/artifact-1/download',
    )
    expect(anchor.download).toBe('trusted.pdf')
  })

  it('rejects session identifiers outside the loaded roster before issuing requests', async () => {
    activateResolvedSession()
    const store = useChatStore()

    const switched = await store.selectSession('session-not-loaded')
    const deleted = await store.deleteSession('session-not-loaded')

    expect(switched).toBe(false)
    expect(deleted).toBe(false)
    expect(store.resolvedSessionId).toBe('session-1')
    expect(chatServiceMock.getHistory).not.toHaveBeenCalled()
    expect(store.errorMessage).toBe('会话不在当前已加载列表中，已阻止操作。')
  })

  it('does not fabricate an artifact download path when the backend omits it', async () => {
    activateResolvedSession()
    const store = useChatStore()
    bindArtifactOwnership(store)
    store.currentWorkspace!.artifacts[0]!.downloadUrl = ''

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
      downloadUrl: '',
    })

    expect(chatServiceMock.downloadArtifact).not.toHaveBeenCalled()
    expect(store.errorMessage).toContain('后端未返回产物下载地址')
  })

  it('shows artifact preview load failures in the current session error', async () => {
    chatServiceMock.getArtifactPreview.mockRejectedValue(
      new ApiError('API Error: 400', 400, {
        code: 'workspace_manifest_invalid',
      }),
    )
    activateResolvedSession()
    const store = useChatStore()
    bindArtifactOwnership(store)

    const preview = await store.loadArtifactPreview('artifact-1')

    expect(preview).toBeNull()
    expect(store.errorMessage).toBe(
      '加载产物预览失败：工作区清单无效，请刷新后重试或联系管理员检查产物目录。',
    )
  })

  it('shows artifact download failures in the current session error', async () => {
    chatServiceMock.downloadArtifact.mockRejectedValue(new ApiError('API Error: 403', 403, null))
    activateResolvedSession()
    const store = useChatStore()
    bindArtifactOwnership(store)

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
      downloadUrl: '/api/aigateway/artifact/artifact-1/download',
    })

    expect(store.errorMessage).toBe('下载产物失败：当前账号没有访问该功能的权限。')
  })

  it('shows attachment upload failures in the current session error', async () => {
    chatServiceMock.uploadFile.mockRejectedValue(new ApiError('API Error: 403', 403, null))
    activateResolvedSession()
    const store = useChatStore()

    const uploaded = await store.uploadSessionFile(new Blob(['input']) as File)

    expect(uploaded).toBeNull()
    expect(store.errorMessage).toBe('上传附件失败：当前账号没有访问该功能的权限。')
  })
})
