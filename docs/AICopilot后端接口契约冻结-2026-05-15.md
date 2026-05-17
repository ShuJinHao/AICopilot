# AICopilot 后端接口契约冻结记录

日期：2026-05-15

## 范围

本记录只覆盖 AICopilot 后端接口契约，不包含 `src/vues` 前端实现，不包含 Cloud/Edge 项目改造。

## 稳定接口组

- `/api/aigateway/language-model*`：模型配置只返回 `hasApiKey`、`apiKeyPreview`，不返回 raw key。
- `/api/aigateway/runtime*`：运行时配置由后端返回有效值，前端不自行推断安全边界。
- `/api/aigateway/session*`、`/api/aigateway/message*`：会话列表包含标题、摘要、最后消息时间、消息数和最近任务元数据。
- `/api/aigateway/upload`：上传由后端校验 scope、owner、扩展名、content-type、MIME、大小、文件名和 SHA256。
- `/api/aigateway/agent/task*`：任务状态、审批状态、canRun/canRetry/canSubmitFinalReview/canApproveFinal 由后端计算。
- `/api/aigateway/tools*`：Tool Registry 管理接口只暴露注册信息，不暴露执行 payload 全文。
- `/api/aigateway/workspace/{code}`：返回 draft/final 文件、artifacts 和 manifest。
- `/api/aigateway/artifact/{id}/download`：后端按当前用户和 task/workspace 关系校验下载权限。
- `/api/rag/*`：知识库 list/search/upload/update/delete/doc list/doc governance 均按 owner/scope/Admin 权限过滤。

## Agent task DTO

`AgentTaskDto` 稳定字段包括：

- `id`、`taskCode`、`sessionId`、`title`、`goal`、`taskType`
- `status`、`riskLevel`、`modelId`、`workspaceId`、`workspaceCode`
- `planJson`，其中 `cloudReadonlyIntent` 是兼容新增字段
- `finalSummary`、`lastFailureReason`、`failureSummary`
- `pendingApprovalCount`
- `canRun`、`canRetry`、`canSubmitFinalReview`、`canApproveFinal`
- `steps[]`

`failureSummary` 仅在失败或拒绝时返回，包含：

- `stepIndex`
- `toolCode`
- `errorCode`
- `safeMessage`
- `canRetry`
- `nextAction`

## Tool execution query

新增只读接口：

`GET /api/aigateway/agent/task/{id}/tool-executions`

Query：

- `pageIndex`
- `pageSize`
- `status`
- `toolCode`

Response：

- `items[]`：`ToolExecutionRecordDto`
- `pageIndex`
- `pageSize`
- `totalCount`
- `totalPages`
- `hasPrevious`
- `hasNext`

权限：

- 使用 `AiGateway.GetAgentTask`
- 先按当前用户校验 task 可见性；不可见时返回 NotFound，不泄露其他用户 task/tool record。

脱敏规则：

- 不返回 API Key、token、password、secret、连接串、服务器绝对路径。
- 不返回 SQL 片段或表名。
- 只返回 `inputSummary`、`outputSummary`、`errorCode`、`errorMessage`、`auditMetadata` 的安全摘要。

## Audit summary

`GET /api/aigateway/agent/task/{id}/audit-summary` 保持列表返回形态，并合并：

- plan
- approval decision
- tool execution audit
- tool execution record
- artifact download
- workspace finalize
- failure summary

新增 action code：

- `Agent.ToolExecutionRecord`
- `Agent.FailureSummary`

## Workspace manifest

`GET /api/aigateway/workspace/{code}` 返回 `manifest[]`，每一项包含：

- `artifactId`
- `type`
- `name`
- `relativePath`
- `status`
- `version`
- `generatedByStep`
- `downloadUrl`
- `createdAt`

规则：

- draft/final 路径分离。
- final artifact 不可修改。
- 生成失败不得创建占位 artifact。
- downloadUrl 只由后端计算。

## 错误码

本批补齐：

- `tool_execution_not_found`
- `artifact_finalized`
- `artifact_generation_failed`
- `workspace_manifest_invalid`

沿用：

- `tool_not_registered`
- `tool_disabled`
- `tool_blocked`
- `tool_permission_denied`
- `tool_requires_approval`
- `cloud_readonly_tool_disabled`
- `cloud_readonly_intent_unsupported`

## 剩余边界

- CloudReadonly 工具仍默认 disabled，启用后仍需 approval、Tool Registry、Cloud read-only policy 和 runtime 二次校验。
- MCP 本批只做 registry 与可观测口径，不新增动态执行器。
- 前端 dirty 内容是外部阻塞项，本批未处理。
