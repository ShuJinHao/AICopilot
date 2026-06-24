import { createServer } from 'node:http'
import { spawn } from 'node:child_process'

const apiPort = 5058
const webPort = 5178
const now = new Date().toISOString()

const permissions = [
  'AiGateway.CreateSession',
  'AiGateway.GetSession',
  'AiGateway.GetListSessions',
  'AiGateway.Chat',
  'AiGateway.GetLanguageModel',
  'AiGateway.GetListLanguageModels',
  'AiGateway.CreateLanguageModel',
  'AiGateway.UpdateLanguageModel',
  'AiGateway.DeleteLanguageModel',
  'AiGateway.GetConversationTemplate',
  'AiGateway.GetConversationTemplateByName',
  'AiGateway.GetListConversationTemplates',
  'AiGateway.CreateConversationTemplate',
  'AiGateway.UpdateConversationTemplate',
  'AiGateway.DeleteConversationTemplate',
  'Rag.GetEmbeddingModel',
  'Rag.GetListEmbeddingModels',
  'Rag.GetKnowledgeBase',
  'Rag.GetListKnowledgeBases',
  'Rag.GetListDocuments',
  'Rag.CreateEmbeddingModel',
  'Rag.UpdateEmbeddingModel',
  'Rag.DeleteEmbeddingModel',
  'Rag.CreateKnowledgeBase',
  'Rag.UpdateKnowledgeBase',
  'Rag.DeleteKnowledgeBase',
  'Rag.UploadDocument',
  'Rag.DeleteDocument',
  'Rag.SearchKnowledgeBase',
  'Identity.GetListPermissions',
  'Identity.GetListRoles',
  'Identity.GetListAuditLogs',
  'Identity.CreateRole',
  'Identity.UpdateRole',
  'Identity.DeleteRole',
  'Identity.GetListUsers',
  'Identity.CreateUser',
  'Identity.UpdateUserRole',
  'Identity.DisableUser',
  'Identity.EnableUser',
  'Identity.ResetUserPassword'
]

const session = {
  id: 'smoke-session',
  title: '产线异常分析',
  onsiteConfirmedAt: now,
  onsiteConfirmedBy: 'admin',
  onsiteConfirmationExpiresAt: new Date(Date.now() + 30 * 60_000).toISOString()
}

const agentTask = {
  id: 'task-1',
  taskCode: 'AGT-0001',
  sessionId: session.id,
  title: 'DEV-001 日志根因分析计划',
  goal: '查看 DEV-001 最近 24 小时设备日志，并给出根因线索',
  taskType: 'PlanDraft',
  status: 'Draft',
  riskLevel: 'Low',
  modelId: 'lm1',
  workspaceId: null,
  workspaceCode: null,
  planJson: JSON.stringify({
    planKind: 'PlanDraft',
    isExecutable: false,
    skillName: '设备日志分析',
    visibleToolCount: 0,
    capabilityGaps: ['确认执行后才检查可执行工具目录。'],
    queryMode: null
  }),
  finalSummary: null,
  createdAt: now,
  updatedAt: now,
  completedAt: null,
  pendingApprovalCount: 0,
  canRun: false,
  lastFailureReason: null,
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
  steps: [
    {
      id: 'step-1',
      stepIndex: 1,
      title: '理解目标并确认设备范围',
      description: '识别 DEV-001、时间范围和只读分析边界',
      stepType: 'Plan',
      status: 'Draft',
      toolCode: null,
      requiresApproval: false,
      errorMessage: null
    },
    {
      id: 'step-2',
      stepIndex: 2,
      title: '规划日志读取和异常时间线汇总',
      description: '确认后才检查日志查询工具和 schema',
      stepType: 'Plan',
      status: 'Draft',
      toolCode: null,
      requiresApproval: false,
      errorMessage: null
    },
    {
      id: 'step-3',
      stepIndex: 3,
      title: '输出根因线索和下一步建议',
      description: '确认后进入可执行计划并由 Worker 执行',
      stepType: 'Plan',
      status: 'Draft',
      toolCode: null,
      requiresApproval: false,
      errorMessage: null
    }
  ]
}

let agentTaskState = agentTask

function toExecutablePlanTask(task) {
  return {
    ...task,
    status: 'PlanApproved',
    planJson: JSON.stringify({
      planKind: 'ExecutablePlan',
      isExecutable: true,
      skillName: '设备日志分析',
      visibleToolCount: 2,
      capabilityGaps: [],
      queryMode: 'CloudReadonly'
    }),
    canRun: true,
    updatedAt: new Date().toISOString(),
    steps: task.steps.map((step) => ({
      ...step,
      status: 'Pending',
      stepType: 'Tool',
      toolCode: step.stepIndex === 1 ? 'resolve_device' : step.stepIndex === 2 ? 'query_device_logs' : 'generate_markdown_report'
    }))
  }
}

function toQueuedTask(task) {
  return {
    ...task,
    status: 'PlanApproved',
    canRun: false,
    isRunQueued: true,
    queuedRunId: 'run-smoke-1',
    runQueueStatus: 'Queued',
    updatedAt: new Date().toISOString()
  }
}

const agentApproval = {
  id: 'approval-agt-1',
  taskId: agentTask.id,
  workspaceCode: null,
  type: 'Tool',
  targetId: 'step-2',
  targetName: 'generate_pdf',
  riskLevel: 'High',
  status: 'Pending',
  reason: '生成高风险正式格式草稿',
  requestedAt: now,
  decidedAt: null,
  decidedBy: null
}

const artifactWorkspace = {
  id: 'workspace-1',
  workspaceCode: agentTask.workspaceCode,
  taskId: agentTask.id,
  status: 'ReadyForFinalize',
  files: [
    { name: 'chart-data.json', relativePath: 'charts/chart-data.json', isDirectory: false, fileSize: 128, updatedAt: now },
    { name: 'report.md', relativePath: 'draft/report.md', isDirectory: false, fileSize: 512, updatedAt: now }
  ],
  artifacts: [
    {
      id: 'artifact-chart',
      name: 'chart-data.json',
      type: 'Json',
      status: 'Draft',
      relativePath: 'charts/chart-data.json',
      fileSize: 128,
      mimeType: 'application/json',
      version: 1,
      updatedAt: now,
      previewKind: 'chart',
      downloadUrl: '/api/aigateway/artifact/artifact-chart/download',
      generatedByStepOrder: 1,
      requiresApproval: false,
      approvalStatus: 'Approved',
      finalizedAt: null,
      artifactVersion: 1,
      artifactStatus: 'Draft',
      sourceMode: 'SimulationBusiness',
      boundary: 'AgentArtifactWorkspace',
      isSimulation: true,
      isSandbox: false,
      sourceLabel: 'AI 独立模拟业务库',
      queryHash: 'sha256:smoke-query-chart',
      resultHash: 'sha256:smoke-result-chart',
      rowCount: 2,
      isTruncated: false
    },
    {
      id: 'artifact-md',
      name: 'report.md',
      type: 'Markdown',
      status: 'Draft',
      relativePath: 'draft/report.md',
      fileSize: 512,
      mimeType: 'text/markdown',
      version: 1,
      updatedAt: now,
      previewKind: 'markdown',
      downloadUrl: '/api/aigateway/artifact/artifact-md/download',
      generatedByStepOrder: 1,
      requiresApproval: false,
      approvalStatus: 'Approved',
      finalizedAt: null,
      artifactVersion: 1,
      artifactStatus: 'Draft',
      sourceMode: 'SimulationBusiness',
      boundary: 'AgentArtifactWorkspace',
      isSimulation: true,
      isSandbox: false,
      sourceLabel: 'AI 独立模拟业务库',
      queryHash: 'sha256:smoke-query-report',
      resultHash: 'sha256:smoke-result-report',
      rowCount: 2,
      isTruncated: false
    }
  ]
}

const agentAuditSummary = [
  {
    id: 'audit-1',
    taskId: agentTask.id,
    workspaceCode: agentTask.workspaceCode,
    actionCode: 'Agent.Plan',
    targetType: 'AgentTask',
    targetName: agentTask.title,
    result: 'Succeeded',
    summary: '生成 Agent 计划',
    createdAt: now,
    metadata: { taskId: agentTask.id, workspaceCode: agentTask.workspaceCode }
  },
  {
    id: 'audit-2',
    taskId: agentTask.id,
    workspaceCode: agentTask.workspaceCode,
    actionCode: 'Agent.ToolExecution',
    targetType: 'AgentStep',
    targetName: 'read_uploaded_file',
    result: 'Succeeded',
    summary: '低风险工具执行完成',
    createdAt: now,
    metadata: { taskId: agentTask.id, workspaceCode: agentTask.workspaceCode, toolName: 'read_uploaded_file' }
  }
]

const samples = {
  model: {
    id: 'lm1',
    provider: 'OpenAI',
    protocolType: 'OpenAICompatible',
    name: 'gpt-5.5',
    baseUrl: 'https://api.openai.com',
    maxTokens: 4096,
    contextWindowTokens: 128000,
    maxOutputTokens: 4096,
    temperature: 0.2,
    isEnabled: true,
    usages: ['Chat', 'Routing', 'Planner'],
    hasApiKey: true,
    apiKeyMasked: 'sk-***',
    connectivityStatus: 'Succeeded',
    connectivityCheckedAt: now,
    connectivityError: null
  },
  routingModel: {
    id: 'routing-1',
    name: 'Intent Routing Agent',
    modelId: 'lm1',
    modelName: 'gpt-5.5',
    modelProvider: 'OpenAI',
    isActive: true
  },
  templates: [
    {
      id: 'tpl-intent',
      name: 'IntentRoutingAgent',
      description: '意图识别和 Skill 路由约束',
      systemPrompt: '你是 A助理的意图识别 Agent。可选意图列表：{{$IntentList}}',
      modelId: 'lm1',
      maxTokens: 2048,
      temperature: 0,
      isEnabled: true
    },
    {
      id: 'tpl-planner',
      name: 'agent_planner',
      description: '受控 Agent 计划生成约束',
      systemPrompt: '你是 A助理的计划生成 Agent。只能输出计划，不能调用工具。',
      modelId: 'lm1',
      maxTokens: 4096,
      temperature: 0,
      isEnabled: true
    },
    {
      id: 'tpl-executor',
      name: 'agent_executor',
      description: '受控 Agent 步骤执行约束',
      systemPrompt: '你是 A助理的最终执行 Agent。只能执行已经确认或审批的计划步骤。',
      modelId: 'lm1',
      maxTokens: 4096,
      temperature: 0.2,
      isEnabled: true
    }
  ],
  embeddingModel: {
    id: 'em1',
    name: 'text-embedding',
    provider: 'OpenAI',
    baseUrl: 'https://api.openai.com',
    modelName: 'text-embedding-3-large',
    dimensions: 3072,
    maxTokens: 8192,
    isEnabled: true,
    hasApiKey: true,
    apiKeyMasked: 'sk-***'
  },
  knowledgeBase: {
    id: 'kb1',
    name: '设备运维知识库',
    description: '报警、配方、巡检只读资料',
    embeddingModelId: 'em1',
    documentCount: 2
  }
}

function sendJson(response, data, status = 200) {
  response.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': 'Content-Type, Authorization',
    'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS'
  })
  response.end(JSON.stringify(data))
}

function sendEvent(response, chunk) {
  response.write(`data: ${JSON.stringify(chunk)}\n\n`)
}

function sendChatStream(response) {
  response.writeHead(200, {
    'Content-Type': 'text/event-stream; charset=utf-8',
    'Cache-Control': 'no-cache',
    Connection: 'keep-alive',
    'Access-Control-Allow-Origin': '*'
  })

  sendEvent(response, {
    source: 'FinalAgentRunExecutor',
    type: 'Text',
    content: '已完成只读分析，未执行控制或写入动作。'
  })
  sendEvent(response, {
    source: 'DataAnalysisExecutor',
    type: 'Widget',
    content: JSON.stringify({
      id: 'chart-1',
      type: 'Chart',
      title: 'LINE-A 产能趋势',
      description: '只读查询结果',
      data: {
        category: 'Line',
        dataset: {
          dimensions: ['time', 'output', 'defect'],
          source: [
            { time: '08:00', output: 120, defect: 2 },
            { time: '09:00', output: 132, defect: 1 }
          ]
        },
        encoding: { x: 'time', y: ['output', 'defect'] }
      }
    })
  })
  sendEvent(response, {
    source: 'DataAnalysisExecutor',
    type: 'Widget',
    content: JSON.stringify({
      id: 'table-1',
      type: 'DataTable',
      title: '设备状态明细',
      description: '最近状态记录',
      data: {
        columns: [
          { key: 'device', label: '设备', dataType: 'string' },
          { key: 'status', label: '状态', dataType: 'string' }
        ],
        rows: [
          { device: 'DEV-001', status: 'Running' },
          { device: 'DEV-002', status: 'Idle' }
        ]
      }
    })
  })
  sendEvent(response, {
    source: 'DataAnalysisExecutor',
    type: 'Widget',
    content: JSON.stringify({
      id: 'stats-1',
      type: 'StatsCard',
      title: '良品率',
      description: '只读统计',
      data: { label: '良品率', value: 98.6, unit: '%' }
    })
  })
  sendEvent(response, {
    source: 'DataAnalysisExecutor',
    type: 'Widget',
    content: JSON.stringify({
      id: 'unknown-1',
      type: 'UnknownWidget',
      title: '未知组件',
      description: '用于验证降级显示',
      data: {}
    })
  })
  sendEvent(response, {
    source: 'FinalAgentRunExecutor',
    type: 'ApprovalRequest',
    content: JSON.stringify({
      callId: 'approval-1',
      name: '高风险工具确认',
      runtimeName: 'mcp',
      targetType: 'McpServer',
      targetName: 'readonly-maintenance',
      toolName: 'diagnose_alarm',
      args: { device: 'DEV-001' },
      requiresOnsiteAttestation: true,
      attestationExpiresAt: new Date(Date.now() + 30 * 60_000).toISOString()
    })
  })
  response.write('data: [DONE]\n\n')
  response.end()
}

function sendPlanStream(response) {
  response.writeHead(200, {
    'Content-Type': 'text/event-stream; charset=utf-8',
    'Cache-Control': 'no-cache',
    Connection: 'keep-alive',
    'Access-Control-Allow-Origin': '*'
  })

  sendEvent(response, {
    source: 'PlanAgentTaskStreamHandler',
    type: 'AgentEvent',
    content: JSON.stringify({
      stage: 'plan_draft_started',
      detail: 'PlanDraft generation started.',
      recoverable: true,
      suggestedAction: null,
      metadata: {
        executesCloudQuery: 'false',
        executesMcpTool: 'false',
        queuesWorker: 'false'
      }
    })
  })
  sendEvent(response, {
    source: 'PlanAgentTaskStreamHandler',
    type: 'Text',
    content: '我已生成计划草案：设备日志分析\n计划包含 3 个步骤，确认前不会启动 Worker。\n'
  })
  sendEvent(response, {
    source: 'PlanAgentTaskStreamHandler',
    type: 'AgentTask',
    content: JSON.stringify(agentTask)
  })
  response.write('data: [DONE]\n\n')
  response.end()
}

const api = createServer((request, response) => {
  const path = new URL(request.url ?? '/', `http://${request.headers.host ?? '127.0.0.1'}`).pathname

  if (request.method === 'OPTIONS') {
    response.writeHead(204, {
      'Access-Control-Allow-Origin': '*',
      'Access-Control-Allow-Headers': 'Content-Type, Authorization',
      'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS'
    })
    response.end()
    return
  }

  if (request.method === 'POST' && path === '/api/aigateway/chat') {
    sendChatStream(response)
    return
  }

  if (request.method === 'POST' && path === '/api/aigateway/agent/task/plan-stream') {
    sendPlanStream(response)
    return
  }

  if (request.method === 'POST' && path === '/api/aigateway/agent/task/approve-plan') {
    agentTaskState = toExecutablePlanTask(agentTaskState)
    sendJson(response, agentTaskState)
    return
  }

  if (request.method === 'POST' && path === '/api/aigateway/agent/task/run') {
    agentTaskState = toQueuedTask(agentTaskState)
    sendJson(response, agentTaskState)
    return
  }

  if (request.method === 'POST' && path === '/api/aigateway/agent/task/retry') {
    agentTaskState = toQueuedTask({
      ...agentTaskState,
      status: 'PlanApproved',
      canRetry: false
    })
    sendJson(response, agentTaskState)
    return
  }

  if (request.method === 'POST' && path === '/api/aigateway/approval/decision') {
    sendChatStream(response)
    return
  }

  const routes = {
    '/api/identity/initialization-status': {
      hasAdminRole: true,
      hasUserRole: true,
      bootstrapAdminConfigured: true,
      hasAdminUser: true,
      isInitialized: true
    },
    '/api/identity/login': { userName: 'admin', token: 'smoke-token' },
    '/api/identity/me': {
      userId: 'u1',
      userName: 'admin',
      roleName: '系统管理员',
      permissions
    },
    '/api/identity/permission/list': permissions.map((code, index) => ({
      code,
      group: code.split('.')[0],
      displayName: code,
      description: `权限 ${index + 1}`
    })),
    '/api/identity/role/list': [
      {
        roleId: 'r1',
        roleName: '系统管理员',
        permissions,
        isSystemRole: true,
        assignedUserCount: 1
      }
    ],
    '/api/identity/user/list': [
      { userId: 'u1', userName: 'admin', roleName: '系统管理员', isEnabled: true, status: 'Enabled' }
    ],
    '/api/identity/audit-log/list': {
      items: [
        {
          id: 'a1',
          actionGroup: 'Config',
          actionCode: 'Update',
          targetType: 'Model',
          targetId: 'm1',
          targetName: 'gpt',
          operatorUserName: 'admin',
          operatorRoleName: '系统管理员',
          result: 'Succeeded',
          summary: '更新模型配置',
          changedFields: ['name'],
          createdAt: now
        }
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1
    },
    '/api/aigateway/session/list': [session],
    '/api/aigateway/session': session,
    '/api/aigateway/skills': [
      {
        id: 'skill-general',
        skillCode: 'general_report',
        displayName: '通用报告',
        description: '默认分析与报告 Skill',
        allowedToolCodes: ['read_uploaded_file', 'generate_markdown_report', 'finalize_artifacts'],
        riskLevel: 'High',
        approvalPolicy: 'ToolApproval',
        allowedDataSourceModes: ['SimulationBusiness'],
        allowedKnowledgeScopes: ['SelectedKnowledgeBase'],
        outputComponentTypes: ['chart', 'markdown', 'pdf'],
        isEnabled: true,
        isBuiltIn: true,
        version: 1,
        createdAt: now,
        updatedAt: now
      },
      {
        id: 'skill-knowledge',
        skillCode: 'knowledge_research',
        displayName: '知识检索',
        description: '知识库检索与摘要 Skill',
        allowedToolCodes: ['rag_search', 'generate_markdown_report', 'finalize_artifacts'],
        riskLevel: 'Low',
        approvalPolicy: 'FinalOutputApproval',
        allowedDataSourceModes: [],
        allowedKnowledgeScopes: ['SelectedKnowledgeBase'],
        outputComponentTypes: ['markdown'],
        isEnabled: true,
        isBuiltIn: true,
        version: 1,
        createdAt: now,
        updatedAt: now
      }
    ],
    '/api/aigateway/chat-message/list': {
      items: [
        {
          messageId: 1,
          sequence: 1,
          sessionId: session.id,
          role: 'User',
          content: '查看 LINE-A 当前设备状态',
          createdAt: now,
          renderChunks: [
            {
              source: 'User',
              type: 'Text',
              content: '查看 LINE-A 当前设备状态'
            }
          ]
        },
        {
          messageId: 2,
          sequence: 2,
          sessionId: session.id,
          role: 'Assistant',
          content: '历史回答：已完成只读分析。',
          createdAt: now,
          renderChunks: [
            {
              source: 'FinalAgentRunExecutor',
              type: 'Text',
              content: '历史回答：已完成只读分析。'
            },
            {
              source: 'DataAnalysisExecutor',
              type: 'Widget',
              content: JSON.stringify({
                id: 'history-widget-1',
                type: 'StatsCard',
                title: '历史异常数',
                description: '来自持久化 renderChunks',
                data: { label: '历史异常数', value: 3, unit: '条' }
              })
            },
            {
              source: 'FinalAgentRunExecutor',
              type: 'ApprovalRequest',
              content: JSON.stringify({
                callId: 'history-approval-1',
                name: 'history_approval',
                runtimeName: 'history_approval',
                targetType: 'McpServer',
                targetName: 'readonly-maintenance',
                toolName: 'history_approval',
                args: {},
                requiresOnsiteAttestation: false
              })
            }
          ]
        }
      ],
      beforeSequence: 1,
      afterSequence: 2,
      hasMore: false,
      hasMoreBefore: false,
      hasMoreAfter: false
    },
    '/api/aigateway/session/timeline': {
      items: [
        {
          sequence: 3,
          eventType: 'AgentTaskPlanCreated',
          createdAt: now,
          agentTaskId: agentTaskState.id,
          agentTaskTitle: agentTaskState.title,
          agentTaskGoal: agentTaskState.goal,
          agentTaskStatus: agentTaskState.status,
          approvalRequestId: null,
          approvalType: null,
          approvalStatus: null,
          approvalTargetName: null,
          artifactWorkspaceId: null,
          workspaceCode: null,
          workspaceStatus: null
        }
      ],
      beforeSequence: 3,
      afterSequence: 3,
      hasMore: false,
      hasMoreBefore: false,
      hasMoreAfter: false
    },
    '/api/aigateway/approval/pending': [],
    '/api/aigateway/agent/task/by-session': [agentTaskState],
    '/api/aigateway/agent/task/task-1/approvals': [],
    '/api/aigateway/agent/approval/pending': [],
    '/api/aigateway/agent/task/task-1/audit-summary': agentAuditSummary,
    '/api/aigateway/workspace/WS-SMOKE-001': artifactWorkspace,
    '/api/aigateway/artifact/artifact-chart/preview': {
      artifactId: 'artifact-chart',
      name: 'chart-data.json',
      artifactType: 'Json',
      previewKind: 'chart',
      artifactStatus: 'Draft',
      artifactVersion: 1,
      relativePath: 'charts/chart-data.json',
      fileSize: 128,
      mimeType: 'application/json',
      sourceMode: 'SimulationBusiness',
      boundary: 'AgentArtifactWorkspace',
      isSimulation: true,
      isSandbox: false,
      sourceLabel: 'AI 独立模拟业务库',
      queryHash: 'sha256:smoke-query-chart',
      resultHash: 'sha256:smoke-result-chart',
      rowCount: 2,
      isTruncated: false,
      content: '{ "sourceLabel": "AI 独立模拟业务库", "queryHash": "sha256:smoke-query-chart" }',
      columns: [],
      rows: [],
      metadata: {}
    },
    '/api/aigateway/artifact/artifact-chart/download': {
      labels: ['08:00', '09:00', '10:00'],
      values: [120, 132, 118],
      source: '模拟 Cloud 只读数据',
      sourceMode: 'Simulation',
      isSimulation: true,
      sourceLabel: '模拟 Cloud 只读数据'
    },
    '/api/aigateway/language-model/list': [samples.model],
    '/api/aigateway/language-model': samples.model,
    '/api/aigateway/routing-model/list': [samples.routingModel],
    '/api/aigateway/routing-model': samples.routingModel,
    '/api/aigateway/conversation-template/list': samples.templates,
    '/api/aigateway/conversation-template': samples.templates[0],
    '/api/rag/embedding-model/list': [samples.embeddingModel],
    '/api/rag/embedding-model': samples.embeddingModel,
    '/api/rag/knowledge-base/list': [samples.knowledgeBase],
    '/api/rag/knowledge-base': samples.knowledgeBase,
    '/api/rag/document/retry': {},
    '/api/rag/document/list': [
      {
        id: 1,
        knowledgeBaseId: 'kb1',
        name: '报警处置手册.pdf',
        extension: '.pdf',
        status: 'Indexed',
        chunkCount: 128,
        errorMessage: null,
        createdAt: now,
        processedAt: now
      }
    ],
    '/api/rag/search': [
      {
        text: 'M-01 报警需要先确认温度和最近配方变更记录。',
        score: 0.91,
        documentId: 1,
        documentName: '报警处置手册.pdf'
      }
    ]
  }

  sendJson(response, routes[path] ?? {})
})

api.listen(apiPort, '127.0.0.1')

const isWindows = process.platform === 'win32'
const viteCommand = isWindows ? process.env.ComSpec || 'cmd.exe' : 'npm'
const viteArgs = isWindows
  ? ['/d', '/s', '/c', `npm.cmd run dev -- --host 127.0.0.1 --port ${webPort}`]
  : ['run', 'dev', '--', '--host', '127.0.0.1', '--port', String(webPort)]
const vite = spawn(viteCommand, viteArgs, {
  stdio: 'inherit',
  env: {
    ...process.env,
    aicopilot_httpapi_http: `http://127.0.0.1:${apiPort}`
  }
})

function shutdown() {
  api.close()
  vite.kill()
}

process.on('SIGINT', shutdown)
process.on('SIGTERM', shutdown)
vite.on('exit', (code) => {
  api.close()
  process.exit(code ?? 0)
})
