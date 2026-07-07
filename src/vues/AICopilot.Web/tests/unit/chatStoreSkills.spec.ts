import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '@/services/apiClient'
import { useChatStore } from '@/stores/chatStore'
import { useSessionStore } from '@/stores/sessionStore'
import { ChunkType, MessageRole } from '@/types/protocols'

const chatServiceMock = vi.hoisted(() => ({
  getSkills: vi.fn(),
  getToolCatalog: vi.fn(),
  getKnowledgeBases: vi.fn(),
  planAgentTaskStream: vi.fn(),
  approveAgentTaskPlan: vi.fn(),
  decideAgentApproval: vi.fn(),
  runAgentTask: vi.fn(),
  retryAgentTask: vi.fn(),
  getAgentTasksBySession: vi.fn(),
  getAgentTaskApprovals: vi.fn(),
  getAgentTaskAuditSummary: vi.fn(),
  getTimeline: vi.fn(),
  getArtifactPreview: vi.fn(),
  uploadFile: vi.fn(),
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

function mockPlanAgentTaskStream(task = plannedTask) {
  chatServiceMock.getAgentTasksBySession.mockResolvedValue([task])
  chatServiceMock.planAgentTaskStream.mockImplementation(async (_payload, callbacks) => {
    callbacks.onChunkReceived({
      source: 'PlanAgentTaskStreamHandler',
      type: ChunkType.Text,
      content: '正在理解目标并识别可用能力...\n'
    })
    callbacks.onChunkReceived({
      source: 'PlanAgentTaskStreamHandler',
      type: ChunkType.Text,
      content: `我已生成计划草案：${task.title}\n`
    })
    callbacks.onChunkReceived({
      source: 'PlanAgentTaskStreamHandler',
      type: ChunkType.AgentTask,
      content: JSON.stringify(task)
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
    mockPlanAgentTaskStream(plannedTask)
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
    chatServiceMock.getArtifactPreview.mockResolvedValue(null)
    chatServiceMock.uploadFile.mockResolvedValue({ id: 'upload-1', name: 'input.txt' })
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

    expect(chatServiceMock.planAgentTaskStream).toHaveBeenCalledWith(expect.objectContaining({
      sessionId: 'session-1',
      goal: '查看 Cloud 设备日志',
      skillCode: null
    }), expect.any(Object))
  })

  it('sends the selected skill code when planning an agent task', async () => {
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-1')
    const store = useChatStore()
    await store.loadSkills()
    store.selectSkill('knowledge_research')

    await store.planAgentTask('查手册')

    expect(chatServiceMock.planAgentTaskStream).toHaveBeenCalledWith(expect.objectContaining({
      sessionId: 'session-1',
      goal: '查手册',
      skillCode: 'knowledge_research'
    }), expect.any(Object))
  })

  it('adds plan mode goal and PlanDraft reply to the conversation flow', async () => {
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-1')
    const store = useChatStore()
    mockPlanAgentTaskStream(createTask({
      title: '设备日志分析',
      goal: '查看 DEV-001 最近 24 小时日志',
      status: 'WaitingPlanApproval',
      planJson: JSON.stringify({
        planKind: 'PlanDraft',
        isExecutable: false,
        skillName: '设备日志分析',
        capabilityGaps: ['No enabled and authorized tools are currently available for this PlanDraft.']
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
          errorMessage: null
        }
      ]
    }))

    await store.planAgentTask('查看 DEV-001 最近 24 小时日志')

    expect(store.currentMessages).toHaveLength(2)
    expect(store.currentMessages[0]?.role).toBe(MessageRole.User)
    expect(store.currentMessages[0]?.chunks[0]?.content).toBe('查看 DEV-001 最近 24 小时日志')
    expect(store.currentMessages[1]?.role).toBe(MessageRole.Assistant)
    expect(store.currentMessages[1]?.isStreaming).toBe(false)
    expect(store.currentMessages[1]?.chunks[0]?.content).toContain('我已生成计划草案')
    expect(store.latestAgentTask?.title).toBe('设备日志分析')
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

    expect(chatServiceMock.planAgentTaskStream).toHaveBeenLastCalledWith(expect.objectContaining({
      skillCode: 'knowledge_research',
      knowledgeBaseIds: ['kb-2']
    }), expect.any(Object))

    store.selectSkill('data_analysis')

    await store.planAgentTask('分析产能')

    expect(chatServiceMock.planAgentTaskStream).toHaveBeenLastCalledWith(expect.objectContaining({
      skillCode: 'data_analysis',
      knowledgeBaseIds: []
    }), expect.any(Object))
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
    expect(chatServiceMock.planAgentTaskStream).toHaveBeenCalledWith(expect.objectContaining({
      skillCode: null,
      preferredToolCodes: ['rag_search']
    }), expect.any(Object))
  })

  it('treats a tool catalog response without tools as an empty catalog', async () => {
    const store = useChatStore()

    await store.loadPluginTools()
    store.togglePluginTool('rag_search')
    chatServiceMock.getToolCatalog.mockResolvedValueOnce({
      version: 2,
      availableToolCount: 0,
      riskSummary: {}
    })

    await expect(store.loadPluginTools()).resolves.toBeUndefined()

    expect(store.availablePluginTools).toEqual([])
    expect(store.selectedToolCodes).toEqual([])
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
    expect(store.errorMessage).toBe('当前没有可用 DataWorker，请检查 Worker 状态。')
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
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-1')
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
    expect(store.errorMessage).toContain('后端未返回产物下载地址')
  })

  it('shows artifact preview load failures in the current session error', async () => {
    chatServiceMock.getArtifactPreview.mockRejectedValue(new ApiError('API Error: 400', 400, {
      code: 'workspace_manifest_invalid'
    }))
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-1')
    const store = useChatStore()

    const preview = await store.loadArtifactPreview('artifact-1')

    expect(preview).toBeNull()
    expect(store.errorMessage).toBe('加载产物预览失败：工作区清单无效，请刷新后重试或联系管理员检查产物目录。')
  })

  it('shows artifact download failures in the current session error', async () => {
    chatServiceMock.downloadArtifact.mockRejectedValue(new ApiError('API Error: 403', 403, null))
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-1')
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

    expect(store.errorMessage).toBe('下载产物失败：当前账号没有访问该功能的权限。')
  })

  it('shows attachment upload failures in the current session error', async () => {
    chatServiceMock.uploadFile.mockRejectedValue(new ApiError('API Error: 403', 403, null))
    const sessionStore = useSessionStore()
    sessionStore.persistCurrentSession('session-1')
    const store = useChatStore()

    const uploaded = await store.uploadSessionFile(new Blob(['input']) as File)

    expect(uploaded).toBeNull()
    expect(store.errorMessage).toBe('上传附件失败：当前账号没有访问该功能的权限。')
  })
})
