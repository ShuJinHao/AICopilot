# AICopilot 前端联调契约包

日期：2026-05-17

## 范围

本契约包只覆盖 AICopilot 后端接口、DTO、错误码和前端 mock 示例。不修改 `src/vues`，不修改 Cloud/Edge，不新增 mock server。前端后续可以按本文档和 `/openapi/v1.json` 做工作台重构；安全判断仍以后端返回字段为准。

## 通用规则

- 所有 `/api/aigateway/*`、`/api/rag/*` 业务接口需要登录态。
- 无权访问 task、workspace、artifact、RAG knowledge base 或 document 时，后端返回 NotFound 或权限错误，不泄露标题、片段、来源、文件名或其他用户资源。
- 模型密钥字段只允许返回 `hasApiKey` 和 `apiKeyPreview`，且 `apiKeyPreview` 固定为 `******`。
- DTO、审计、错误、队列、worker status、tool execution 不返回 API Key、token、连接串、服务器绝对路径、SQL/表名或大 payload 全文。
- 这些字段只由后端计算，前端不得自行推断：`canRun`、`canRetry`、`canSubmitFinalReview`、`canApproveFinal`、`isRunQueued`、`isRunInProgress`、`downloadUrl`、`workspaceMatchesHttpApi`。

## 核心路由

### 模型与 runtime

| Method | Path | 用途 | 权限要点 |
| --- | --- | --- | --- |
| GET | `/api/aigateway/language-model/list` | 语言模型列表 | 配置读取权限 |
| GET | `/api/aigateway/language-model` | 单个语言模型 | 配置读取权限 |
| POST | `/api/aigateway/language-model` | 创建语言模型，可接收 API Key | 配置管理权限 |
| PUT | `/api/aigateway/language-model` | 更新语言模型，可接收 API Key | 配置管理权限 |
| POST | `/api/aigateway/language-model/test` | 连接测试 | 配置管理权限 |
| GET | `/api/aigateway/runtime-settings` | runtime settings | 配置读取权限 |
| PUT | `/api/aigateway/runtime-settings` | 更新 runtime settings | 配置管理权限 |

`LanguageModelDto` 稳定字段：`id`、`provider`、`protocolType`、`name`、`baseUrl`、`maxTokens`、`contextWindowTokens`、`maxOutputTokens`、`temperature`、`isEnabled`、`usages`、`hasApiKey`、`apiKeyPreview`、`connectivityStatus`、`connectivityCheckedAt`、`connectivityError`。

`EmbeddingModelDto` 稳定字段：`id`、`name`、`provider`、`baseUrl`、`modelName`、`dimensions`、`maxTokens`、`isEnabled`、`hasApiKey`、`apiKeyPreview`。

### Session 与上传

| Method | Path | 用途 | 权限要点 |
| --- | --- | --- | --- |
| POST | `/api/aigateway/session` | 创建会话 | 登录用户 |
| GET | `/api/aigateway/session/list` | 会话列表 | 只返回当前用户可见 |
| GET | `/api/aigateway/chat-message/list` | 消息列表 | 当前用户会话 |
| POST | `/api/aigateway/upload` | Agent 上传 | 文件安全策略 + scope/owner |
| GET | `/api/aigateway/upload/list` | 上传记录 | 当前用户可见 |

上传必须经过扩展名、content type、MIME sniffing、大小、文件名净化、SHA256、scope/owner 校验。危险类型直接拒绝并写审计。

### Agent task

| Method | Path | 用途 | 权限要点 |
| --- | --- | --- | --- |
| POST | `/api/aigateway/agent/task/plan` | 创建计划 | Tool Registry + Planner guard |
| POST | `/api/aigateway/agent/task/approve-plan` | 批准 plan | 任务可见 + approval |
| POST | `/api/aigateway/agent/task/run` | 入队运行 | 任务 owner + run lease |
| POST | `/api/aigateway/agent/task/retry` | 失败任务重试入队 | 仅 Failed 且 workspace 未 final |
| POST | `/api/aigateway/agent/task/cancel` | 取消任务 | 关闭 queue/attempt/approval |
| GET | `/api/aigateway/agent/task` | 单个任务 | 当前用户可见 |
| GET | `/api/aigateway/agent/task/by-session` | 会话任务列表 | 当前用户会话 |

`AgentTaskDto` 稳定字段：`id`、`taskCode`、`sessionId`、`title`、`goal`、`taskType`、`status`、`riskLevel`、`modelId`、`workspaceId`、`planJson`、`finalSummary`、`createdAt`、`updatedAt`、`completedAt`、`steps`、`workspaceCode`、`pendingApprovalCount`、`lastFailureReason`、`canRun`、`canRetry`、`canSubmitFinalReview`、`canApproveFinal`、`failureSummary`、`activeRunAttemptId`、`runAttemptCount`、`isRunInProgress`、`queuedRunId`、`runQueueStatus`、`isRunQueued`。

`planJson` 兼容字段：`plannerMode`、`plannerModelId`、`plannerValidationVersion`、`plannerToolCatalogVersion`、`plannerAvailableToolCount`、`cloudReadonlyIntent`、`steps[].inputJson`。

`AgentTaskStatus`：`Draft`、`WaitingPlanApproval`、`PlanApproved`、`Running`、`WaitingToolApproval`、`GeneratingArtifacts`、`WorkspaceReady`、`WaitingFinalApproval`、`Finalized`、`Completed`、`Rejected`、`Failed`、`Cancelled`。

`AgentStepStatus`：`Pending`、`WaitingApproval`、`Approved`、`Running`、`Completed`、`Failed`、`Skipped`、`Cancelled`。

### Approval

| Method | Path | 用途 | 权限要点 |
| --- | --- | --- | --- |
| GET | `/api/aigateway/agent/approval/pending` | Agent pending approval | 当前用户可见 |
| GET | `/api/aigateway/agent/task/{id}/approvals` | 任务 approval 列表 | 任务可见 |
| POST | `/api/aigateway/agent/approval/{id}/approve` | 批准 Agent approval | 审批权限 |
| POST | `/api/aigateway/agent/approval/{id}/reject` | 拒绝 Agent approval | 审批权限 |

`AgentApprovalType`：`Plan`、`ToolCall`、`Artifact`、`FinalOutput`。

`AgentApprovalStatus`：`Pending`、`Approved`、`Rejected`、`Cancelled`、`Expired`。

ToolCall 审批通过后，后端自动入队 `ApprovalResume`。FinalOutput 仍必须通过 workspace final review/finalize 闭环，不自动发布 final。

### Workspace 与 artifact

| Method | Path | 用途 | 权限要点 |
| --- | --- | --- | --- |
| GET | `/api/aigateway/workspace/{code}` | workspace manifest | task owner |
| POST | `/api/aigateway/workspace/{code}/submit-final-review` | 提交最终确认 | task owner |
| POST | `/api/aigateway/workspace/{code}/finalize` | FinalOutput approval 后发布 final | task owner + approval |
| GET | `/api/aigateway/artifact/{id}/download` | artifact 下载 | task/workspace/artifact 可见 |

`ArtifactWorkspaceDto.manifest[]` 字段：`artifactId`、`type`、`name`、`relativePath`、`status`、`version`、`generatedByStep`、`downloadUrl`、`createdAt`。

规则：draft/final 分离；final artifact 不可修改；重试产物写 draft 新版本或 attempt suffix；生成失败不得伪造 artifact；`downloadUrl` 只由后端计算。

### Tool Registry 与执行记录

| Method | Path | 用途 | 权限要点 |
| --- | --- | --- | --- |
| GET | `/api/aigateway/tools` | 工具列表 | `AiGateway.ToolRegistry.Read` |
| GET | `/api/aigateway/tools/{toolCode}` | 单工具 | `AiGateway.ToolRegistry.Read` |
| PATCH | `/api/aigateway/tools/{toolCode}` | 更新 enabled/approval/risk/schema | `AiGateway.ToolRegistry.Manage` |
| GET | `/api/aigateway/agent/task/{id}/tool-executions` | 任务工具执行记录 | task 可见 |

`ToolRegistrationDto` 字段：`id`、`toolCode`、`displayName`、`description`、`providerType`、`targetType`、`targetName`、`inputSchemaJson`、`outputSchemaJson`、`riskLevel`、`requiredPermission`、`requiresApproval`、`isEnabled`、`timeoutSeconds`、`auditLevel`、`createdAt`、`updatedAt`、`runtimeAvailable`、`lastDiscoveredAt`、`sourceServerName`。

`ToolExecutionRecordDto` 字段：`id`、`taskId`、`stepId`、`runAttemptId`、`toolCode`、`inputSummary`、`outputSummary`、`status`、`startedAt`、`completedAt`、`durationMs`、`errorCode`、`errorMessage`、`artifactId`、`auditMetadata`。

CloudReadonly 工具默认 disabled；启用后仍必须经过 approval、Tool Registry、Cloud read-only policy、runtime 二次校验。MCP 工具默认 disabled、requiresApproval，只有 enabled 且 runtimeAvailable 才能进入动态计划。

### Run queue 与 worker status

| Method | Path | 用途 | 权限要点 |
| --- | --- | --- | --- |
| GET | `/api/aigateway/agent/task/{id}/run-attempts` | task attempt 历史 | task 可见 |
| GET | `/api/aigateway/agent/task/{id}/run-queue` | task queue 历史 | task 可见 |
| GET | `/api/aigateway/agent/run-queue` | 全局队列 | Admin |
| GET | `/api/aigateway/agent/run-queue/summary` | 队列汇总 | Admin |
| GET | `/api/aigateway/agent/worker/status` | worker 健康 | Admin |
| POST | `/api/aigateway/agent/run-queue/{id}/dead-letter` | 安全转 DeadLetter | Admin |

`AgentTaskRunQueueStatus`：`Queued`、`Leased`、`Succeeded`、`Failed`、`Cancelled`、`DeadLetter`。

`AgentTaskRunAttemptStatus`：`Running`、`WaitingApproval`、`Succeeded`、`Failed`、`Cancelled`。

`AgentWorkerStatusDto` 字段：`statusCode`、`hasActiveWorkers`、`workspaceConsistent`、`httpApiWorkspaceRootHash`、`activeWorkerCount`、`queuedCount`、`leasedCount`、`staleLeasedCount`、`oldestQueuedAt`、`generatedAt`、`workers`。

`AgentWorkerHeartbeatDto` 字段：`id`、`workerId`、`workerName`、`startedAt`、`lastSeenAt`、`isActive`、`activeQueueItemId`、`activeTaskId`、`workspaceRootHash`、`version`、`workspaceMatchesHttpApi`。

### RAG

| Method | Path | 用途 | 权限要点 |
| --- | --- | --- | --- |
| GET | `/api/rag/embedding-model/list` | embedding model 列表 | RAG 配置读取 |
| POST | `/api/rag/embedding-model` | 创建 embedding model | RAG 配置管理 |
| GET | `/api/rag/knowledge-base/list` | knowledge base 列表 | 仅返回可见 KB |
| GET | `/api/rag/knowledge-base` | 单 KB | 无权返回 NotFound |
| POST | `/api/rag/knowledge-base` | 创建 KB | 默认 OwnerOnly |
| PUT | `/api/rag/knowledge-base` | 更新 KB | owner/Admin |
| DELETE | `/api/rag/knowledge-base` | 删除 KB | owner/Admin |
| POST | `/api/rag/document` | 文档上传 | KB 权限 + 文件安全策略 |
| GET | `/api/rag/document/list` | 文档列表 | KB 权限 |
| PUT | `/api/rag/document/governance` | 文档治理字段 | KB 权限 |
| POST | `/api/rag/search` | 检索 | KB 权限 |

普通用户只能访问自己 `OwnerOnly` 或 `AuthenticatedUsers` scope 的知识库；Admin 可访问全部。无权 get/search/upload/update/delete/doc list/doc governance 返回 NotFound 或权限错误，不泄露标题、文档名、片段或来源。

## Mock 示例

### 计划任务

```json
{
  "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "taskCode": "agt_202605170001",
  "sessionId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "title": "设备状态报告",
  "goal": "生成设备状态报告",
  "taskType": "CloudDataReport",
  "status": "WaitingToolApproval",
  "riskLevel": "Medium",
  "modelId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
  "workspaceId": "dddddddd-dddd-dddd-dddd-dddddddddddd",
  "workspaceCode": "ws_202605170001",
  "pendingApprovalCount": 1,
  "canRun": true,
  "canRetry": false,
  "canSubmitFinalReview": false,
  "canApproveFinal": false,
  "activeRunAttemptId": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
  "runAttemptCount": 1,
  "isRunInProgress": true,
  "queuedRunId": "ffffffff-ffff-ffff-ffff-ffffffffffff",
  "runQueueStatus": "Leased",
  "isRunQueued": true,
  "planJson": "{\"plannerMode\":\"Dynamic\",\"plannerModelId\":\"cccccccc-cccc-cccc-cccc-cccccccccccc\",\"plannerValidationVersion\":\"2026-05\",\"plannerToolCatalogVersion\":\"2026-05\",\"plannerAvailableToolCount\":3,\"cloudReadonlyIntent\":{\"intent\":\"Analysis.Device.Status\",\"confidence\":0.91},\"steps\":[{\"title\":\"查询设备状态\",\"toolCode\":\"query_cloud_data_readonly\",\"requiresApproval\":true,\"inputJson\":{\"target\":\"Device\",\"deviceCode\":\"D-001\"}}]}",
  "steps": [
    {
      "id": "11111111-1111-1111-1111-111111111111",
      "stepIndex": 1,
      "title": "查询设备状态",
      "description": "调用 Cloud 只读工具",
      "stepType": "DataQuery",
      "status": "WaitingApproval",
      "toolCode": "query_cloud_data_readonly",
      "requiresApproval": true,
      "errorMessage": null
    }
  ]
}
```

### 入队运行

```json
{
  "queuedRunId": "ffffffff-ffff-ffff-ffff-ffffffffffff",
  "runQueueStatus": "Queued",
  "isRunQueued": true,
  "isRunInProgress": false
}
```

### 工具审批

```json
{
  "id": "22222222-2222-2222-2222-222222222222",
  "taskId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "approvalType": "ToolCall",
  "status": "Pending",
  "targetId": "query_cloud_data_readonly",
  "safeSummary": "Cloud 只读查询：设备 D-001 状态",
  "createdAt": "2026-05-17T01:05:00Z"
}
```

### Workspace

```json
{
  "workspaceCode": "ws_202605170001",
  "status": "Draft",
  "manifest": [
    {
      "artifactId": "33333333-3333-3333-3333-333333333333",
      "type": "Markdown",
      "name": "设备状态报告.md",
      "relativePath": "draft/report-attempt-1.md",
      "status": "Draft",
      "version": 1,
      "generatedByStep": 2,
      "downloadUrl": "/api/aigateway/artifact/33333333-3333-3333-3333-333333333333/download",
      "createdAt": "2026-05-17T01:10:00Z"
    }
  ]
}
```

### Run queue summary

```json
{
  "queuedCount": 1,
  "leasedCount": 0,
  "succeededCount": 2,
  "failedCount": 0,
  "cancelledCount": 0,
  "deadLetterCount": 0,
  "staleLeasedCount": 0,
  "oldestQueuedAt": "2026-05-17T01:15:00Z",
  "activeWorkerCount": 1,
  "generatedAt": "2026-05-17T01:15:30Z"
}
```

### Worker status

```json
{
  "statusCode": "healthy",
  "hasActiveWorkers": true,
  "workspaceConsistent": true,
  "httpApiWorkspaceRootHash": "sha256:httpapi",
  "activeWorkerCount": 1,
  "queuedCount": 1,
  "leasedCount": 0,
  "staleLeasedCount": 0,
  "oldestQueuedAt": "2026-05-17T01:15:00Z",
  "generatedAt": "2026-05-17T01:15:30Z",
  "workers": [
    {
      "workerId": "worker-1",
      "workerName": "AICopilot.DataWorker",
      "isActive": true,
      "workspaceRootHash": "sha256:httpapi",
      "workspaceMatchesHttpApi": true
    }
  ]
}
```

### RAG 无权访问

```json
{
  "code": "missing_permission",
  "message": "Resource was not found or is not visible."
}
```

### 模型密钥脱敏

```json
{
  "id": "44444444-4444-4444-4444-444444444444",
  "provider": "OpenAICompatible",
  "protocolType": "OpenAICompatible",
  "name": "planner",
  "baseUrl": "https://models.example.invalid/v1",
  "isEnabled": true,
  "usages": ["Chat", "Planner"],
  "hasApiKey": true,
  "apiKeyPreview": "******",
  "connectivityStatus": "Ready"
}
```

## 错误码目录

### Auth

- `account_disabled`
- `session_revoked`
- `user_missing`
- `missing_permission`
- `invalid_credentials`
- `unauthorized`
- `cloud_oidc_not_configured`
- `cloud_oidc_invalid_principal`
- `cloud_identity_inactive`
- `cloud_identity_unverified`
- `external_identity_conflict`

### App

- `rate_limit_exceeded`
- `chat_context_expired`
- `chat_configuration_missing`
- `chat_stream_failed`
- `approval_stream_failed`
- `approval_already_processed`
- `approval_pending`
- `capability_not_allowed`
- `control_action_blocked`
- `token_budget_exceeded`
- `onsite_presence_required`
- `onsite_presence_expired`
- `approval_reconfirmation_required`
- `tool_not_registered`
- `tool_disabled`
- `tool_blocked`
- `tool_permission_denied`
- `tool_requires_approval`
- `tool_input_invalid`
- `tool_execution_timeout`
- `cloud_readonly_tool_disabled`
- `cloud_readonly_intent_unsupported`
- `planner_model_unavailable`
- `planner_tool_catalog_empty`
- `planner_tool_schema_unsupported`
- `agent_plan_invalid`
- `agent_plan_tool_denied`
- `agent_plan_schema_invalid`
- `tool_execution_not_found`
- `artifact_finalized`
- `artifact_generation_failed`
- `workspace_manifest_invalid`
- `agent_task_run_in_progress`
- `agent_task_retry_not_allowed`
- `agent_task_run_lease_expired`
- `agent_task_cancellation_requested`
- `agent_task_run_queued`
- `agent_task_run_queue_not_found`
- `agent_task_run_queue_lease_expired`
- `agent_worker_unavailable`
- `agent_worker_workspace_mismatch`
- `agent_run_queue_dead_letter_not_allowed`
- `agent_run_queue_operation_denied`

### Cloud AI Read

- `cloud_ai_read_not_configured`
- `cloud_ai_read_request_blocked`
- `cloud_ai_read_unauthorized`
- `cloud_ai_read_forbidden`
- `cloud_ai_read_not_found`
- `cloud_ai_read_unavailable`
- `cloud_ai_read_missing_required_parameter`

## 验收

- `/openapi/v1.json` 必须包含核心 AICopilot/RAG 路由和 HTTP 方法。
- DTO snapshot 必须保留前端依赖字段。
- 文档错误码必须与后端常量保持一致。
- 示例 payload 不得包含 API Key、token、连接串、服务器绝对路径、SQL/表名。
- 当前 `src/vues` dirty 内容仍为外部阻塞项，本批不处理，也不恢复前端源码断言测试。
