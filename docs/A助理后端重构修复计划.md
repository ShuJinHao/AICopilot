# A助理后端重构修复计划

版本：v1.0
执行对象：Codex / 后端 Agent
范围：AICopilot 后端、AI Gateway、Agent Runtime、数据库、MCP/工具链、RAG、上传、安全、验收
不包含：前端视觉重构、Cloud 业务写操作、Edge Client 改造

---

## 0. 后端总目标

把当前 A助理后端从“受控产物 Agent MVP 骨架”修成“可被前端稳定调用、可内部试用、可审计、可审批、可清库重建”的后端系统。

核心目标：

- AI 模块可以清库重建。
- 数据库 schema 清晰。
- 默认 seed 完整。
- A助理身份固定。
- 模型/API 可动态配置。
- 历史轮次可配置。
- RAG 权限隔离。
- 上传文件安全。
- Agent 计划结构化。
- 工具调用受控。
- Cloud 数据只读。
- 产物工作区受控。
- 审批严格。
- 审计完整。
- 接口契约稳定。
- 测试覆盖正向和负向闭环。

---

## 1. 后端执行边界

必须保持：

- 不修改 Cloud 业务写接口。
- 不开放任意 shell。
- 不允许写任意服务器路径。
- 不允许模型绕过工具白名单。
- 不允许模型绕过审批。
- 不允许模型写 Cloud 业务数据。
- 不允许旧身份出现在提示词、种子、文案、测试数据中。
- 不允许日志、审计、前端 DTO 泄露 API Key、连接串、服务器绝对路径。

可以做：

- AI Gateway 数据库清库重建。
- AI Gateway 迁移重建。
- AI Gateway 表结构重构。
- AI Gateway 内置配置重建。
- AICopilot 后端接口重构。
- Agent Runtime 重构。
- RAG 权限和上传链路重构。
- 受控 workspace 重构。

---

## 2. P0：PR 元信息修复

当前 PR 已经不是文档 PR，必须先修正 PR 信息。

任务：

- 更新 PR 标题，例如：`[codex] Rebuild A助理 controlled artifact Agent MVP`
- 更新 PR body。
- 写清楚：
  - 后端新增内容
  - 前端新增内容
  - 数据库变化
  - 测试结果
  - Cloud/Edge 未修改边界
  - 剩余风险
  - 人工验收步骤
- 保持 Draft，直到 P0 和 P1 完成。
- 删除或改写“Documentation-only / No build required”描述。

验收：

- reviewer 看 PR body 就知道这是一次 Agent MVP 重构。
- PR body 与实际 100+ 文件变更一致。
- 不误导为单纯文档变更。

---

## 3. P0：AI Gateway 数据库清理和 fresh seed

用户允许 AI 数据库删库，因此不要保守地堆迁移。

任务：

1. 盘点当前 AI Gateway 表结构。
2. 保留最终领域对象。
3. 删除无意义的中间迁移。
4. 重新生成干净初始迁移，或生成一次最终迁移。
5. 新增 fresh database seed。
6. 新增 fresh database seed 测试。

建议最终核心表：

```text
aigateway.language_models
aigateway.conversation_templates
aigateway.routing_model_configurations
aigateway.chat_runtime_settings
aigateway.sessions
aigateway.messages
aigateway.agent_tasks
aigateway.agent_steps
aigateway.agent_tool_registry
aigateway.agent_tool_executions
aigateway.approval_policies
aigateway.approval_requests
aigateway.artifact_workspaces
aigateway.artifacts
aigateway.upload_records
aigateway.rag_access_bindings
aigateway.provider_reliability_events
```

seed 必须包含：

- 默认 `ChatRuntimeSettings`
- A助理内置模板
- 默认审批策略
- 默认工具注册
- 默认权限
- 默认 workspace 配置
- 默认 routing 配置
- 默认空模型提示，或 disabled 示例模型

seed 不得包含：

- 旧身份
- 明文真实 API Key
- 真实生产连接串
- Cloud 写工具
- 任意 shell 工具

验收：

- 清库后应用启动成功。
- 清库后配置页能加载。
- 清库后能创建会话。
- 清库后能重置模板。
- 清库后能生成 Agent 计划。
- 清库后能上传文件。
- 清库后能创建 workspace。
- FreshDatabaseSeedTests 通过。

---

## 4. P0：A助理提示词和身份治理

任务：

- 确保所有内置模板只使用“A助理”身份。
- 增加源码扫描测试：
  - `朝小夕`
  - `朝夕`
  - 其他旧身份变体
- 扫描范围：
  - C# 源码
  - Vue/TS 源码
  - Seed 数据
  - Markdown 验收文档可排除历史讨论，但正式模板不得包含
  - 测试 fixture
- 新增命令：重置内置模板。
- 新增启动 seed：缺模板时自动创建。
- 内置模板增加版本号。
- 管理员修改模板后仍可一键恢复内置模板。
- 模板 DTO 返回：
  - code
  - name
  - scope
  - version
  - isBuiltIn
  - updatedAt
  - modelId

验收：

- 旧身份扫描通过。
- 数据库 reset 后模板为 A助理。
- 普通回答、RAG、Agent planner、executor、artifact、failure 都有独立模板。
- AiEval 增加身份守卫测试。

---

## 5. P0：动态模型/API 和密钥安全

当前模型动态配置已有雏形，但密钥安全不足。

任务：

### 5.1 模型配置

支持字段：

- provider
- protocolType
- displayName
- modelName
- baseUrl
- apiKeySecretRef 或 encryptedApiKey
- usage
- isEnabled
- contextWindowTokens
- maxOutputTokens
- temperature
- timeoutSeconds
- supportsVision
- supportsTools
- supportsStreaming
- connectivityStatus
- connectivityCheckedAt
- connectivityError

### 5.2 密钥安全

必须做：

- API Key 前端永不回显明文。
- DTO 只返回 `hasApiKey`、`apiKeyPreview`。
- 日志和审计不得记录明文 Key。
- 连接测试错误必须替换 Key。
- 数据库存储至少加密；如果暂时无 secret manager，也要增加保护层。
- 更新 Key、清空 Key、保留原 Key 三种行为要明确。

### 5.3 连接测试

测试结果：

- succeeded
- failed
- unsupportedProtocol
- missingApiKey
- timeout
- rateLimited

验收：

- 可新增 OpenAI-compatible API。
- 可新增 Anthropic-compatible API。
- 可新增本地模型 API。
- 可测试连接。
- 可禁用模型。
- 聊天模型列表只返回 enabled + usage=Chat。
- Routing 只使用 enabled + usage=Routing。
- 不泄露 Key。

---

## 6. P0：运行时设置和历史轮次

任务：

- 保留全局 runtime settings。
- 支持管理员更新：
  - routingHistoryCount
  - answerHistoryCount
  - ragRewriteHistoryCount
  - agentPlanningHistoryCount
  - summaryThresholdMessages
  - contextTokenLimit
- 后端返回当前生效设置。
- 所有使用历史的地方都必须读取配置，不得写死。
- 增加 token budget。
- 增加上下文截断策略。
- 增加历史摘要基础设施。

下一步增强：

- 会话摘要表。
- 任务摘要表。
- 长期记忆开关。
- 跨会话记忆开关。
- 管理员可关闭长期记忆。

验收：

- 路由历史使用配置。
- 最终回答历史使用配置。
- Agent planner 历史使用配置。
- RAG rewrite 历史使用配置。
- 设置越界会被 clamp。
- 设置修改后立即生效或明确重启要求。

---

## 7. P0：会话历史和标题

任务：

- 第一条用户消息后自动生成标题。
- 标题最大长度固定。
- 保存 lastMessageSummary。
- 保存 lastMessageAt。
- 保存 messageCount。
- 前端发送第一条消息后可刷新 session list。
- 后端提供 rename session。
- 后端提供 session list 分页。
- 后端提供 session search。
- 后端支持按更新时间排序。
- 后端返回当前 session 关联的最近 agent task summary。

建议 SessionDto：

```json
{
  "id": "guid",
  "title": "string",
  "lastMessageSummary": "string",
  "lastMessageAt": "datetime",
  "messageCount": 10,
  "latestAgentTaskStatus": "WorkspaceReady",
  "latestWorkspaceCode": "ws_xxx",
  "artifactCount": 3
}
```

验收：

- 新会话发第一条消息后不再一直显示“新会话”。
- 历史列表有摘要和时间。
- 删除会话后相关历史不可见。
- 无权访问别人 session。
- 会话消息顺序正确。

---

## 8. P0：上传安全

当前上传能力必须加固。

任务：

### 8.1 文件类型白名单

默认允许：

- `.csv`
- `.xlsx`
- `.json`
- `.txt`
- `.md`
- `.pdf`
- `.docx`
- `.png`
- `.jpg`
- `.jpeg`
- `.webp`

默认禁止：

- `.exe`
- `.bat`
- `.cmd`
- `.ps1`
- `.sh`
- `.dll`
- `.so`
- `.jar`
- `.zip`，除非后续有专门安全处理
- `.rar`
- `.7z`
- `.sql`
- `.js`，除非作为文本并禁用执行
- `.html` 上传到浏览器预览时要严格 sandbox

### 8.2 校验

- 扩展名校验。
- content-type 校验。
- MIME sniffing。
- 文件大小限制。
- 按类型大小限制。
- 文件名净化。
- SHA256 记录。
- 上传 scope 校验。
- 上传所有权校验。
- 危险文件返回明确错误码。
- 上传失败也写审计。

### 8.3 图片

图片上传要记录：

- width
- height
- mimeType
- size
- 是否允许视觉模型读取
- 是否进入知识库

第一阶段可以先只支持图片上传和缩略图，不强制接视觉模型。

验收：

- 危险文件拒绝。
- 超大文件拒绝。
- 无权限 session/task 上传拒绝。
- 多文件上传接口或多次上传均可。
- 上传后能绑定 session/task。
- 上传图片可在 DTO 中被识别为 image。
- 上传到知识库必须校验知识库权限。

---

## 9. P0：RAG 权限隔离

任务：

- SearchKnowledgeBase 必须校验当前用户是否有 KnowledgeBase 权限。
- Upload to KnowledgeBase 必须校验当前用户是否有写入权限。
- Agent plan 传入 knowledgeBaseIds 时，后端必须逐一校验。
- RAG 结果不得返回无权文档。
- 无权时不得泄露文档标题。
- RAG 返回来源：
  - knowledgeBaseId
  - documentId
  - documentName
  - chunkIndex
  - score
  - lowConfidence
  - text excerpt
- 回答引用来源必须使用检索结果，不得伪造。
- 增加 RAG audit。

验收：

- 用户 A 不能搜用户 B 的知识库。
- 无权限 knowledgeBaseId 在 Agent plan 阶段被拒绝。
- 删除文档后不再命中。
- 低置信度显示。
- 无来源时模型说明未检索到可靠来源。
- RAG 权限测试通过。

---

## 10. P0：Agent 状态机统一

建议 AgentTaskStatus：

```text
Draft
WaitingPlanApproval
PlanApproved
Running
WaitingToolApproval
GeneratingArtifacts
WorkspaceReady
WaitingFinalApproval
Finalized
Completed
Rejected
Failed
Cancelled
```

建议 AgentStepStatus：

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

建议 ApprovalStatus：

```text
Pending
Approved
Rejected
Expired
Cancelled
```

建议 ApprovalType：

```text
Plan
ToolCall
Artifact
FinalOutput
```

任务：

- 后端 enum 统一。
- DTO enum 统一。
- 前端显示映射统一。
- 测试状态流转。
- 不再出现前后端不同名状态，例如 Tool/ToolCall、ReadyForFinalize/WorkspaceReady 混用。

验收：

- 状态流转测试通过。
- 前端不会显示未知英文状态。
- 驳回状态不会继续执行。
- 失败可重试时必须明确 canRetry。

---

## 11. P0：finalize 与审批拆分

当前风险：用户点击 finalize 可能等同于批准 FinalOutput。

目标：拆成两个动作。

推荐流程：

```text
草稿产物生成
-> WorkspaceReady
-> 用户点击“提交最终确认”
-> 创建 FinalOutput ApprovalRequest
-> WaitingFinalApproval
-> 有权限审批人批准
-> 系统复制 draft 到 final
-> Finalized / Completed
```

接口建议：

- `POST /workspace/{code}/submit-final-review`
- `POST /agent/approval/{id}/approve`
- `POST /workspace/{code}/finalize` 只允许系统内部或有审批权限后调用
- 或者 approve FinalOutput 后由后端自动 finalize

规则：

- 没有 FinalOutput approval approved，不得写 final。
- 普通用户无审批权限时只能提交审批。
- 有审批权限的人可以“批准并输出”。
- 驳回 final 后不得 final。
- final 后 draft 可保留，但 final 文件不可被修改。
- final 二次生成必须新版本或新 workspace。

验收：

- 未审批 final 目录为空。
- 驳回 final 不生成正式文件。
- 审批通过后才生成 final。
- 无权限用户不能批准 final。
- 审计记录 submit、approve、finalize 三个动作。

---

## 12. P1：动态 Agent Planner

当前固定 plan 必须升级。

任务：

### 12.1 Planner 输入

输入包括：

- user goal
- session history
- uploaded files
- selected knowledge bases
- available tools
- allowed artifact types
- runtime settings
- user permissions
- safety policy

### 12.2 Planner 输出 JSON

示例：

```json
{
  "title": "最近一周产线异常分析报告",
  "goal": "分析最近一周产线数据并生成报告",
  "taskType": "ReportGeneration",
  "requiresUserConfirmation": true,
  "expectedArtifacts": ["Markdown", "Chart", "Pdf"],
  "steps": [
    {
      "title": "读取上传文件",
      "toolCode": "read_uploaded_file",
      "arguments": { "uploadIds": ["..."] },
      "riskLevel": "Low",
      "requiresApproval": false
    }
  ]
}
```

### 12.3 后端校验

后端必须校验：

- JSON schema。
- 工具是否注册。
- 工具是否启用。
- 用户是否有权限。
- 参数是否符合 schema。
- 风险是否正确或由系统重算。
- 高风险审批是否补齐。
- 输出产物类型是否允许。
- 是否出现未知路径。
- 是否要求 Cloud 写入。
- 是否要求 shell。

### 12.4 安全降级

如果 planner 失败：

- 返回可理解错误。
- 使用安全固定 plan fallback。
- 记录 planner failure audit。
- 不直接执行。

验收：

- 模型输出未知工具被拒绝。
- 模型输出 shell 被拒绝。
- 模型要求写 Cloud 被拒绝。
- 模型输出非法 JSON 被拒绝或 fallback。
- 合法计划可展示、确认、执行。

---

## 13. P1：Tool Registry / MCP 基础

当前工具不应长期硬编码 switch。

任务：

新增 Tool Registry：

```text
toolCode
displayName
description
providerType: BuiltIn | MCP | CloudReadonly | Artifact
inputSchemaJson
outputSchemaJson
riskLevel
requiredPermission
requiresApproval
isEnabled
timeoutSeconds
maxRetries
auditLevel
createdAt
updatedAt
```

工具执行记录：

```text
executionId
taskId
stepId
toolCode
inputSummary
outputSummary
status
startedAt
completedAt
durationMs
errorCode
errorMessage
artifactId
auditMetadata
```

MCP 基础：

- mcp server 配置。
- mcp server 连接测试。
- 工具列表同步。
- 工具启停。
- 工具 schema 显示。
- MCP 工具默认 disabled，需要管理员启用。
- MCP 工具默认需要审批，除非管理员降低风险。

验收：

- planner 只能使用 enabled 工具。
- 关闭工具后 planner 不可用。
- 工具执行有记录。
- 工具失败可查。
- MCP 未配置时不影响内置工具。

---

## 14. P1：Cloud 只读数据工具

当前 `query_cloud_data_readonly` 不能只返回说明，必须实现真实只读 MVP。

建议最小工具：

- `cloud_read_device_status`
- `cloud_read_device_logs`
- `cloud_read_production_metrics`
- `cloud_read_recipe_versions`

每个工具必须：

- 有输入 schema。
- 有输出 schema。
- 有时间范围限制。
- 有行数限制。
- 有权限校验。
- 只读。
- 审计查询范围。
- 返回结构化数据。
- 不暴露 SQL。
- 不暴露内部表名。
- 不暴露连接串。

示例输入：

```json
{
  "deviceCode": "DEV-001",
  "startTime": "2026-05-01T00:00:00Z",
  "endTime": "2026-05-08T00:00:00Z",
  "limit": 200
}
```

示例输出：

```json
{
  "source": "CloudReadonly",
  "records": [],
  "summary": {
    "count": 120,
    "timeRange": "...",
    "keyFindings": []
  }
}
```

验收：

- 工具能读取真实只读数据或 mock readonly 数据。
- 无权限拒绝。
- 超范围拒绝。
- 结果可用于图表。
- 结果可用于报告。
- 审计可查。
- 不写 Cloud。

---

## 15. P1：Artifact Workspace 和产物生命周期

任务：

- workspace code 必须不可预测或至少不易枚举。
- workspace 查询必须校验用户权限。
- artifact 下载必须校验用户权限。
- draft/final 分离。
- 产物版本化。
- 修改意见生成新版本。
- final 后不可修改。
- 支持 workspace 文件树。
- 支持 manifest。
- 支持 artifact metadata。
- 支持过期清理策略。
- 支持 workspace storage settings。
- 支持下载审计。

建议 artifact 字段：

```text
id
workspaceId
taskId
type
name
relativePath
version
status: Draft | Reviewing | Approved | Final | Rejected | Deleted
mimeType
fileSize
previewKind
createdByStepId
createdAt
updatedAt
finalizedAt
downloadCount
```

验收：

- 路径穿越测试通过。
- draft 只能写 draft/charts/data/source。
- final 只能由审批流程写入。
- 下载无权限失败。
- final 后下载成功。
- 工作区文件树正常。
- 版本不会覆盖旧文件。

---

## 16. P1：产物生成器增强

当前 PDF/PPTX/XLSX 是基础草稿，需要增强但不必一次高保真。

任务：

- Markdown 生成结构化报告。
- HTML 生成可浏览报告。
- Chart 生成 JSON + PNG/SVG。
- PDF 生成基础版。
- PPTX 生成基础版。
- XLSX 生成基础版。
- 文件命名规范。
- 支持中文字体或字体回退。
- 支持图表嵌入。
- 支持来源引用。
- 支持低置信度说明。
- 生成失败返回明确原因。

验收：

- 生成文件可打开。
- 中文不乱码。
- 图表数据正确。
- PDF/PPTX/XLSX 至少有标题、摘要、数据来源、关键指标。
- 失败不伪造产物。
- 产物写入 draft。

---

## 17. P1：审计、日志、观测

必须审计：

- 模型配置新增/修改/删除。
- 模型连接测试。
- 模板重置。
- runtime settings 修改。
- 上传。
- RAG 搜索。
- Agent plan 创建。
- 计划审批。
- 工具调用。
- 工具失败。
- artifact 生成。
- artifact 下载。
- final 提交。
- final 审批。
- workspace finalize。

审计必须脱敏：

- API Key。
- token。
- connection string。
- server absolute path。
- user raw file content。
- RAG full content 可截断。

指标建议：

- model latency
- tool duration
- token usage
- cost estimate
- upload size
- artifact size
- approval wait time
- task success rate
- task failure reason

验收：

- task audit summary 可查。
- workspace audit 可查。
- 失败原因可定位。
- 敏感信息不出现在审计中。

---

## 18. P2：后端接口契约

输出给前端的契约必须稳定。

至少提供：

### 模型

- `GET /api/aigateway/language-model/chat-options`
- `GET /api/aigateway/language-model/list`
- `POST /api/aigateway/language-model/test`
- `POST/PUT/DELETE /api/aigateway/language-model`

### Runtime

- `GET /api/aigateway/runtime-settings`
- `PUT /api/aigateway/runtime-settings`

### Session

- `POST /api/aigateway/session`
- `GET /api/aigateway/session/list`
- `GET /api/aigateway/chat-message/list`
- `PUT /api/aigateway/session/title`

### Upload

- `POST /api/aigateway/upload`
- `GET /api/aigateway/upload/list`

### Agent

- `POST /api/aigateway/agent/task/plan`
- `POST /api/aigateway/agent/task/approve-plan`
- `POST /api/aigateway/agent/task/run`
- `POST /api/aigateway/agent/task/cancel`
- `GET /api/aigateway/agent/task`
- `GET /api/aigateway/agent/task/by-session`
- `GET /api/aigateway/agent/task/{id}/approvals`
- `GET /api/aigateway/agent/task/{id}/audit-summary`

### Approval

- `GET /api/aigateway/agent/approval/pending`
- `POST /api/aigateway/agent/approval/{id}/approve`
- `POST /api/aigateway/agent/approval/{id}/reject`

### Workspace

- `GET /api/aigateway/workspace/{code}`
- `POST /api/aigateway/workspace/{code}/submit-final-review`
- `GET /api/aigateway/artifact/{id}/download`

### RAG

- `GET/POST knowledge base list/search`
- `POST upload to knowledge base`

验收：

- DTO 文档完成。
- 前端 mock 能根据 DTO 构造。
- 错误码统一。
- 接口不要频繁改名。

---

## 19. 后端测试计划

必须新增或强化：

### 单元测试

- PromptGovernanceTests
- RuntimeSettingsTests
- SessionHistoryMetadataTests
- UploadValidationTests
- RagPermissionTests
- AgentPlanSchemaTests
- ToolRegistryTests
- ApprovalStateMachineTests
- ArtifactWorkspaceTests
- SecretRedactionTests

### 集成测试

- FreshDatabaseSeedTests
- AgentPositiveFlowTests
- AgentNegativeFlowTests
- CloudReadonlyToolTests
- WorkspaceFinalizeApprovalTests
- ArtifactDownloadPermissionTests
- RagUnauthorizedAccessTests

### AI Eval

- A助理身份测试
- 不伪造来源测试
- 不伪造文件测试
- 不承诺绕过审批测试
- 不承诺写 Cloud 测试
- Planner 工具白名单测试

### 安全测试

- 路径穿越
- 文件上传危险类型
- API Key 泄露
- RAG 越权
- Workspace 越权
- Artifact 越权
- 未审批 final

---

## 20. 后端最终验收标准

后端完成后必须满足：

- AI Gateway 清库重建通过。
- 模板 seed 正确。
- 旧身份扫描通过。
- 模型配置和测试通过。
- 密钥不泄露。
- 历史轮次配置生效。
- 会话标题和摘要正确。
- 上传安全通过。
- RAG 权限隔离通过。
- Agent planner schema 校验通过。
- 工具白名单通过。
- Cloud 只读工具有真实闭环。
- workspace draft/final 严格分离。
- final 必须审批。
- artifact 下载权限正确。
- 审计摘要完整。
- 所有后端测试通过。
- CI 或验收脚本稳定通过。
