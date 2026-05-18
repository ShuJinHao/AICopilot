# A助理前端分离重构修复计划

版本：v1.0
执行对象：前端 Agent / UI Agent
范围：AICopilot.Web 前端页面、组件、状态管理、交互、上传、Agent 工作台、产物工作区、配置页
不包含：后端安全决策、数据库、Cloud 数据写入、MCP 后端实现

---

## 0. 前端总目标

把当前前端从“聊天页上堆 Agent 功能”重构为：

> A助理 Agent 工作台前端。

前端要清楚展示：

- 当前模型/API
- 当前会话
- 历史会话
- 上传文件/图片
- RAG 知识来源
- Agent 任务计划
- 任务步骤
- 工具调用状态
- 审批队列
- 产物工作区
- draft/final 状态
- 下载入口
- 修改意见入口
- 审计摘要
- 安全边界

前端不负责最终安全判断，所有真正权限、审批、工具、RAG、Cloud 边界必须由后端决定。

---

## 1. 前端执行边界

前端必须遵守：

- 不在前端伪造审批通过。
- 不绕过后端 final 流程。
- 不在前端保存 API Key 明文。
- 不在 localStorage 保存敏感数据。
- 不展示后端未授权的 workspace/artifact。
- 不自行判断 RAG 权限。
- 不自行判断 Cloud 权限。
- 不把 draft 当正式产物。
- 不把模型生成内容显示为“已执行”，除非后端状态确认。
- 不出现“朝小夕”“朝夕”等旧身份。

前端可以做：

- 页面布局重构。
- 组件拆分。
- 状态管理重构。
- API service 重构。
- mock 数据重构。
- 上传交互重构。
- 工作区预览和下载体验。
- 审批交互。
- 配置页重构。
- 响应式优化。
- 前端 smoke 和 unit tests。

---

## 2. 信息架构

建议前端整体结构：

```text
AppShell
├── TopBar
│   ├── 当前系统状态
│   ├── 当前模型选择
│   ├── RAG/Agent/上传状态
│   └── 用户信息
├── LeftSidebar
│   ├── 会话列表
│   ├── 任务历史
│   └── 搜索/筛选
├── MainWorkspace
│   ├── ChatPanel
│   ├── Composer
│   └── MessageTimeline
└── RightPanel
    ├── AgentPlanPanel
    ├── AgentStepsPanel
    ├── ApprovalQueuePanel
    ├── ArtifactSummaryPanel
    ├── AuditSummaryPanel
    └── SafetyBoundaryPanel
```

独立页面：

```text
/config/models
/config/runtime
/config/templates
/config/tools
/config/approval-policies
/config/workspace
/knowledge
/workspace/:workspaceCode
```

---

## 3. 页面拆分建议

### 3.1 ChatShell

负责：

- 三栏布局。
- 移动端抽屉。
- 页面级 loading。
- 当前 session 加载。
- 当前 model 加载。
- 当前 agent task 加载。
- 错误统一展示。

不负责：

- 具体消息渲染。
- Agent 计划逻辑。
- Workspace 文件树逻辑。

---

### 3.2 SessionSidebar

功能：

- 会话列表。
- 自动标题显示。
- 最后一条消息摘要。
- 更新时间。
- 消息数。
- 最新任务状态。
- 产物数量。
- 新建会话。
- 删除会话。
- 重命名会话。
- 搜索会话。
- 按任务状态筛选。

验收：

- 第一条消息后标题能刷新。
- 不再长期显示“新会话”。
- 切换会话时消息、任务、workspace 一起刷新。
- 无历史时显示清楚空状态。

---

### 3.3 ChatPanel

功能：

- 消息列表。
- 流式输出。
- 模型真实信息。
- RAG 来源提示。
- 工具调用块。
- 审批请求块。
- 错误块。
- Widget 块。
- 系统状态块。

要求：

- 用户消息和 A助理消息视觉区分。
- 代码/JSON/表格可读。
- 长文本可折叠。
- 工具调用结果不要误显示成自然语言结论。
- “草稿产物已生成”要链接到 workspace。
- “等待审批”要链接到审批队列。

---

### 3.4 Composer

功能：

- 输入问题。
- 发送消息。
- Shift+Enter 换行。
- 上传按钮。
- 选择任务模式：
  - 普通聊天
  - 数据分析
  - 生成报告
  - 生成 PPT
  - 生成 Excel
  - 自定义 Agent
- 选择是否使用 RAG。
- 选择是否使用上传文件。
- 选择是否创建 Agent 任务。

建议：

- 不要把普通聊天和 Agent 任务完全混淆。
- 用户点击“生成 Agent 计划”时，应明确进入任务流程。
- 普通聊天仍保持简单。

---

## 4. Agent 工作台组件

### 4.1 AgentPlanPanel

展示：

- 任务标题。
- 用户目标。
- 任务类型。
- 预计数据源。
- 预计工具。
- 预计产物。
- 风险等级。
- 是否需要审批。
- 计划 JSON 或结构化步骤。
- 用户确认按钮。
- 修改计划按钮。
- 驳回计划按钮。

交互：

- 计划未确认前，不显示“运行”。
- 计划审批中，按钮 disabled。
- 计划驳回后，显示驳回原因。
- 可复制计划摘要。

---

### 4.2 AgentStepsPanel

展示：

- 步骤序号。
- 步骤标题。
- toolCode。
- 状态。
- 风险等级。
- 是否需要审批。
- 开始时间。
- 完成时间。
- 错误原因。
- 产物链接。

状态显示必须使用后端标准枚举：

```text
Pending
WaitingApproval
Approved
Running
Completed
Failed
Skipped
Cancelled
```

交互：

- 失败步骤可展开看错误。
- 可点击查看工具输入摘要/输出摘要。
- 可点击查看关联 artifact。
- 不要在前端直接重跑单个步骤，除非后端提供接口。

---

### 4.3 ApprovalQueuePanel

展示：

- 计划审批。
- 工具审批。
- 产物审批。
- 最终输出审批。
- 风险等级。
- 审批原因。
- 请求时间。
- 审批人。
- 审批意见。

交互：

- 批准。
- 驳回。
- 填写意见。
- 查看关联计划/步骤/产物。
- 普通用户无审批权限时只显示状态，不显示批准按钮。
- 审批中按钮 loading。
- 审批后刷新 task/workspace/audit。

特别要求：

- 不允许前端自己认为批准成功。
- 必须以后端返回状态为准。
- FinalOutput 审批必须与 workspace final 状态联动。

---

### 4.4 ArtifactSummaryPanel

展示：

- workspaceCode。
- workspace 状态。
- draft 数量。
- final 数量。
- 文件大小。
- 产物类型分组。
- 最近生成时间。
- 下载入口。
- 打开工作区入口。

产物分组：

- 图表
- Markdown
- HTML
- PDF
- PPTX
- XLSX
- CSV
- JSON
- 来源文件
- 日志

交互：

- 下载 draft 时要标记“草稿”。
- 下载 final 时标记“正式”。
- 未 final 的产物不要显示成正式交付。
- 点击产物进入 workspace 页面。

---

### 4.5 AuditSummaryPanel

展示：

- 动作代码。
- 对象类型。
- 对象名称。
- 结果。
- 摘要。
- 时间。
- 失败原因。
- 审批状态。

交互：

- 刷新。
- 筛选成功/失败。
- 查看更多。
- 不展示敏感字段。
- 失败原因要能被用户理解。

---

### 4.6 SafetyBoundaryPanel

展示：

- Cloud 只读。
- 当前用户。
- 当前模型。
- 当前权限。
- 是否启用 RAG。
- 是否启用 Agent。
- 是否有待审批。
- 是否在岗确认。
- 当前 workspace 是否 final。

目的：

- 让用户知道 A助理能做什么，不能做什么。
- 不要占用过多空间。
- 可折叠。

---

## 5. 产物工作区页面

建议新增独立页面：

```text
/workspace/:workspaceCode
```

页面布局：

```text
WorkspaceHeader
├── workspaceCode
├── task title
├── status
├── open/finalized time
├── submit final review / approve final / download final

WorkspaceBody
├── FileTree
├── ArtifactPreview
├── ArtifactMetadata
└── RevisionPanel
```

### 5.1 FileTree

展示固定目录：

- source/
- data/
- charts/
- draft/
- final/
- logs/
- audit/

要求：

- 目录折叠。
- 文件类型图标。
- 文件大小。
- 更新时间。
- 状态标签。
- 点击文件显示详情。
- final 目录明显标识。

### 5.2 ArtifactPreview

预览规则：

- Markdown：前端渲染。
- HTML：iframe sandbox 或纯文本安全预览。
- Chart JSON：图表展示。
- PNG/SVG：图片展示。
- PDF：浏览器新窗口或 iframe。
- PPTX：显示文件信息、页数、下载；第一阶段不强求在线编辑。
- XLSX：显示前几行摘要；第一阶段不强求完整在线表格。
- CSV：显示前几行。
- JSON：格式化展示。
- 其他：下载。

### 5.3 RevisionPanel

功能：

- 输入修改意见。
- 选择要修改的 artifact。
- 提交“重新生成草稿”请求。
- 显示版本历史。
- 显示当前版本。
- 显示上一次修改意见。

注意：

- 前端只提交修改意见。
- 后端决定是否创建新 Agent 步骤。
- 不在前端直接改 PDF/PPTX。

### 5.4 Final 操作

根据权限显示：

- 普通用户：提交最终确认。
- 审批人：批准并输出。
- 已 final：下载正式产物。
- 已驳回：显示原因，允许重新生成草稿。

---

## 6. 上传文件和图片前端

### 6.1 UploadPanel

功能：

- 拖拽上传。
- 点击上传。
- 多文件上传。
- 文件列表。
- 上传进度。
- 上传失败原因。
- 删除未使用上传。
- 文件作用域选择：
  - 本次会话
  - 当前 Agent 任务
  - 加入知识库
- 文件类型提示。
- 文件大小提示。

### 6.2 图片上传

功能：

- 图片缩略图。
- 图片大小。
- 图片类型。
- 是否用于视觉分析。
- 是否加入知识库。
- 删除图片。
- 图片预览。

### 6.3 安全提示

前端显示：

- 允许类型。
- 最大大小。
- 禁止可执行文件。
- 上传后会进入受控工作区。
- 上传到知识库需要权限。

注意：

- 前端校验只是体验，后端必须再次校验。
- 前端不能因为本地校验通过就认为上传安全。

---

## 7. RAG 前端

### 7.1 KnowledgeSourceSelector

功能：

- 知识库列表。
- 权限过滤后的可选知识库。
- 多选。
- 搜索。
- 显示文档数。
- 显示更新时间。
- 显示是否启用。
- 显示 embedding 状态。

### 7.2 RAG 命中展示

在回答或 Agent 任务中显示：

- 使用了哪些知识库。
- 命中文档。
- chunk index。
- score。
- 低置信度提示。
- 查看来源。
- 无来源时提示“未检索到可靠来源”。

### 7.3 知识库上传

功能：

- 上传到知识库。
- 选择知识库。
- 上传进度。
- 入库状态。
- 索引状态。
- 失败原因。

---

## 8. 模型/API 配置前端

### 8.1 LanguageModelConfig

功能：

- 新增模型。
- 编辑模型。
- 删除模型。
- 启用/禁用。
- 选择 provider。
- protocolType。
- baseUrl。
- modelName。
- contextWindowTokens。
- maxOutputTokens。
- temperature。
- usage：Chat/Routing/Embedding/Planner。
- supportsVision。
- supportsTools。
- 连接测试。
- 查看连接状态。
- API Key 脱敏展示。

要求：

- API Key 输入后不回显。
- 保存后显示“已配置/未配置”。
- 清空 Key 必须二次确认。
- 连接错误不显示密钥。

---

## 9. Runtime / Prompt / Tool / Approval 配置页

### 9.1 RuntimeConfig

配置：

- routingHistoryCount
- answerHistoryCount
- ragRewriteHistoryCount
- agentPlanningHistoryCount
- summaryThresholdMessages
- contextTokenLimit

展示：

- 当前生效值。
- 允许范围。
- 保存结果。
- 恢复默认。

### 9.2 ConversationTemplateConfig

功能：

- 模板列表。
- scope。
- version。
- built-in 标记。
- 编辑。
- 启用/禁用。
- 重置内置模板。
- 旧身份扫描结果。

### 9.3 ToolRegistryConfig

功能：

- 工具列表。
- toolCode。
- providerType。
- riskLevel。
- requiredPermission。
- requiresApproval。
- enabled。
- schema 查看。
- 测试连接。
- MCP server 状态。

### 9.4 ApprovalPolicyConfig

功能：

- 审批策略列表。
- 工具风险等级。
- 角色要求。
- 是否自动审批。
- 是否允许提交 final。
- 是否允许批准 final。

---

## 10. 前端状态管理拆分

建议不要所有东西塞进一个 chatStore。

推荐 Pinia store：

```text
sessionStore
messageStore
modelStore
runtimeConfigStore
templateConfigStore
uploadStore
ragStore
agentTaskStore
approvalStore
workspaceStore
auditStore
uiLayoutStore
```

### agentTaskStore

负责：

- tasks by session
- latest task
- plan task
- approve plan
- run task
- cancel task
- refresh task
- task steps

### workspaceStore

负责：

- current workspace
- file tree
- artifacts
- download artifact
- submit final review
- refresh workspace
- preview artifact

### uploadStore

负责：

- upload queue
- upload progress
- session uploads
- task uploads
- knowledge uploads

### ragStore

负责：

- knowledge bases
- selected knowledge bases
- source hits
- low confidence display

---

## 11. 前端错误处理

必须统一错误显示。

错误分类：

- auth error
- permission denied
- model unavailable
- missing api key
- upload rejected
- upload too large
- unsupported file type
- rag permission denied
- agent plan failed
- tool approval required
- tool failed
- workspace not found
- artifact not found
- final approval required
- server error

要求：

- 用户可理解。
- 不显示内部堆栈。
- 不显示密钥。
- 不显示服务器绝对路径。
- 错误要能定位到具体模块。
- 关键失败要有“刷新/重试/联系管理员”的提示。

---

## 12. 响应式和可用性

桌面端：

- 三栏布局。
- 左侧 280-320px。
- 中间自适应。
- 右侧 360-440px。
- 工作区页面可全屏。

移动端：

- 左侧会话变抽屉。
- 右侧 Agent 工作台变抽屉。
- 聊天优先。
- 上传和审批操作可用。
- 产物下载可用。

可访问性：

- 按钮有明确文本或 aria-label。
- loading 状态明确。
- disabled 状态有原因。
- 审批操作二次确认。
- final 操作二次确认。

---

## 13. 前端测试计划

### Unit Tests

- chunkReducer
- messageStore
- sessionStore
- agentTaskStore
- workspaceStore
- uploadStore
- approvalStore
- modelStore
- enum label mapper
- error mapper

### Component Tests

- SessionSidebar
- ChatPanel
- Composer
- AgentPlanPanel
- AgentStepsPanel
- ApprovalQueuePanel
- ArtifactSummaryPanel
- WorkspaceFileTree
- ArtifactPreview
- UploadPanel
- KnowledgeSourceSelector
- LanguageModelConfig

### Smoke / E2E

必须覆盖：

1. 登录。
2. 进入聊天页。
3. 加载模型列表。
4. 创建会话。
5. 发第一条消息后标题刷新。
6. 上传 CSV。
7. 生成 Agent 计划。
8. 确认计划。
9. 审批工具。
10. 生成产物。
11. 打开 workspace。
12. 下载 draft。
13. 提交 final。
14. 审批 final。
15. 下载 final。
16. 驳回计划。
17. 驳回 final。
18. 上传危险文件失败。
19. 无权限 workspace 显示错误。
20. 移动端布局可操作。

---

## 14. 前端接口契约依赖

前端 Agent 开始重构前，需要后端提供稳定 DTO。

最小依赖：

### SessionDto

```ts
interface SessionDto {
  id: string
  title: string
  lastMessageSummary?: string
  lastMessageAt?: string
  messageCount: number
  latestAgentTaskStatus?: string
  latestWorkspaceCode?: string
  artifactCount?: number
}
```

### AgentTask

```ts
interface AgentTask {
  id: string
  sessionId: string
  title: string
  goal: string
  taskType: string
  riskLevel: string
  status: string
  planJson: string
  workspaceCode?: string
  canRun: boolean
  canRetry: boolean
  canSubmitFinalReview: boolean
  steps: AgentStep[]
}
```

### AgentStep

```ts
interface AgentStep {
  id: string
  stepIndex: number
  title: string
  description?: string
  stepType: string
  toolCode?: string
  riskLevel: string
  requiresApproval: boolean
  status: string
  startedAt?: string
  completedAt?: string
  errorMessage?: string
  outputSummary?: string
}
```

### ApprovalRequest

```ts
interface ApprovalRequest {
  id: string
  taskId: string
  workspaceCode?: string
  type: 'Plan' | 'ToolCall' | 'Artifact' | 'FinalOutput'
  targetId: string
  targetName: string
  riskLevel: string
  status: 'Pending' | 'Approved' | 'Rejected' | 'Expired' | 'Cancelled'
  reason?: string
  requestedAt: string
  decidedAt?: string
  decidedBy?: string
}
```

### ArtifactWorkspace

```ts
interface ArtifactWorkspace {
  id: string
  workspaceCode: string
  workspaceUrl: string
  status: string
  files: WorkspaceFile[]
  artifacts: ArtifactRecord[]
}
```

### ArtifactRecord

```ts
interface ArtifactRecord {
  id: string
  workspaceCode: string
  taskId: string
  type: string
  name: string
  relativePath: string
  version: number
  status: 'Draft' | 'Reviewing' | 'Approved' | 'Final' | 'Rejected'
  mimeType: string
  fileSize: number
  previewKind?: string
  downloadUrl: string
  createdAt: string
  updatedAt: string
}
```

---

## 15. 前端不应做的事

不要：

- 在前端硬编码绕过审批。
- 在前端根据 artifact 状态伪造 final。
- 在前端自己拼 Cloud 查询。
- 在前端自己拼 MCP 工具调用。
- 在前端保存明文 API Key。
- 在前端显示服务器绝对路径。
- 在前端直接渲染不可信 HTML，除非 sandbox。
- 在前端把模型计划当作已执行事实。
- 在前端写死 knowledgeBaseIds 为空。
- 在前端写死 taskType 为 ReportGeneration。
- 在前端只支持单文件上传。
- 在前端混用 Tool 和 ToolCall 状态名。

---

## 16. 前端最终验收标准

前端完成后必须满足：

- 页面从聊天页升级为清晰 Agent 工作台。
- 左侧历史会话正常显示标题、摘要、时间。
- 中间聊天可正常流式回答。
- 顶部模型可切换。
- 上传文件/图片体验完整。
- Agent 计划可展示、确认、驳回。
- 步骤状态清晰。
- 审批队列清晰。
- 工作区可独立打开。
- draft/final 清晰区分。
- 产物可下载。
- 修改意见入口存在。
- RAG 来源可展示。
- 配置页可管理模型、runtime、模板、工具、审批策略。
- 错误提示友好。
- 移动端可操作。
- unit/build/smoke 全部通过。
