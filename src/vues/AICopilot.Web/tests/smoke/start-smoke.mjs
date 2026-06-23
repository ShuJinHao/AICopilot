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
  title: 'LINE-A 产物报告',
  goal: '生成只读产物报告',
  taskType: 'ReportGeneration',
  status: 'WaitingApproval',
  riskLevel: 'Medium',
  modelId: 'lm1',
  workspaceId: 'workspace-1',
  workspaceCode: 'WS-SMOKE-001',
  planJson: '{}',
  finalSummary: null,
  createdAt: now,
  updatedAt: now,
  completedAt: null,
  pendingApprovalCount: 1,
  canRun: false,
  lastFailureReason: null,
  canRetry: true,
  canSubmitFinalReview: true,
  canApproveFinal: false,
  failureSummary: null,
  activeRunAttemptId: null,
  runAttemptCount: 1,
  isRunInProgress: false,
  queuedRunId: null,
  runQueueStatus: 'Queued',
  isRunQueued: false,
  steps: [
    {
      id: 'step-1',
      stepIndex: 1,
      title: '读取上传文件',
      description: '读取会话临时输入',
      stepType: 'Tool',
      status: 'Completed',
      toolCode: 'read_uploaded_file',
      requiresApproval: false,
      errorMessage: null
    },
    {
      id: 'step-2',
      stepIndex: 2,
      title: '生成 PDF 草稿',
      description: '写入 draft/ 后等待审批',
      stepType: 'Tool',
      status: 'WaitingApproval',
      toolCode: 'generate_pdf',
      requiresApproval: true,
      errorMessage: null
    }
  ]
}

const agentApproval = {
  id: 'approval-agt-1',
  taskId: agentTask.id,
  workspaceCode: agentTask.workspaceCode,
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
    '/api/aigateway/agent/task/plan': agentTask,
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
          agentTaskId: agentTask.id,
          agentTaskTitle: agentTask.title,
          agentTaskGoal: agentTask.goal,
          agentTaskStatus: agentTask.status,
          approvalRequestId: agentApproval.id,
          approvalType: 'Plan',
          approvalStatus: 'Approved',
          approvalTargetName: agentTask.title,
          artifactWorkspaceId: artifactWorkspace.id,
          workspaceCode: artifactWorkspace.workspaceCode,
          workspaceStatus: artifactWorkspace.status
        },
        {
          sequence: 4,
          eventType: 'AgentTaskStepCompleted',
          createdAt: now,
          agentTaskId: agentTask.id,
          agentTaskTitle: agentTask.title,
          agentTaskStatus: agentTask.status,
          agentStepId: 'step-1',
          agentStepIndex: 1,
          agentStepTitle: '读取上传文件',
          agentStepStatus: 'Completed',
          agentStepToolCode: 'read_uploaded_file',
          artifactWorkspaceId: artifactWorkspace.id,
          workspaceCode: artifactWorkspace.workspaceCode,
          workspaceStatus: artifactWorkspace.status
        },
        {
          sequence: 5,
          eventType: 'ApprovalRequested',
          createdAt: now,
          agentTaskId: agentTask.id,
          agentTaskTitle: agentTask.title,
          agentTaskStatus: agentTask.status,
          agentStepId: 'step-2',
          agentStepIndex: 2,
          agentStepTitle: '生成 PDF 草稿',
          agentStepStatus: 'WaitingApproval',
          approvalRequestId: agentApproval.id,
          approvalType: 'ToolCall',
          approvalStatus: agentApproval.status,
          approvalTargetName: agentApproval.targetName,
          artifactWorkspaceId: artifactWorkspace.id,
          workspaceCode: artifactWorkspace.workspaceCode,
          workspaceStatus: artifactWorkspace.status
        },
        {
          sequence: 6,
          eventType: 'ArtifactReady',
          createdAt: now,
          agentTaskId: agentTask.id,
          agentTaskTitle: agentTask.title,
          agentTaskStatus: agentTask.status,
          artifactWorkspaceId: artifactWorkspace.id,
          workspaceCode: artifactWorkspace.workspaceCode,
          workspaceStatus: artifactWorkspace.status,
          artifactId: 'artifact-chart',
          artifactName: 'chart-data.json',
          artifactType: 'Json',
          artifactStatus: 'Draft',
          artifactRelativePath: 'charts/chart-data.json',
          artifactDownloadUrl: '/api/aigateway/artifact/artifact-chart/download'
        }
      ],
      beforeSequence: 3,
      afterSequence: 6,
      hasMore: false,
      hasMoreBefore: false,
      hasMoreAfter: false
    },
    '/api/aigateway/approval/pending': [],
    '/api/aigateway/agent/task/by-session': [agentTask],
    '/api/aigateway/agent/task/task-1/approvals': [agentApproval],
    '/api/aigateway/agent/approval/pending': [agentApproval],
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
