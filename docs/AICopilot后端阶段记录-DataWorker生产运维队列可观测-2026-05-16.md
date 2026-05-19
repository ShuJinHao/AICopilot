# AICopilot 后端阶段记录：DataWorker 生产运维闭环 + Agent 队列可观测

日期：2026-05-16

## 改动范围

- 仅修改 AICopilot 后端、后端测试和本阶段记录。
- 未修改 `src/vues` 前端。
- 未修改 `IIoT.CloudPlatform`、`IIoT.EdgeClient`。

## 后端改动

- AiGateway fresh baseline 增加 `agent_worker_heartbeats`，记录 workerId、workerName、startedAt、lastSeenAt、active queue/task、workspace root hash、version。
- `AgentTaskRunQueueWorker` 启动轮询、空闲、领取任务、任务结束时更新 heartbeat。
- 新增 workspace root hash 计算，HttpApi 与 DataWorker 只暴露 hash 和健康状态，不暴露服务器绝对路径。
- 新增全局队列只读查询、summary、worker status 查询和 dead-letter 运维动作。
- `AgentTaskRunQueueItem` 增加 dead-letter 状态边界：仅 `Queued`、已过期 `Leased`、`Failed` 可转入 `DeadLetter`。
- AppHost 和测试夹具显式对齐 HttpApi/DataWorker 的 `ArtifactWorkspace__RootPath`，避免 worker 生成产物和 API 下载侧路径不一致。

## 接口变化

- 新增：
  - `GET /api/aigateway/agent/run-queue`
  - `GET /api/aigateway/agent/run-queue/summary`
  - `GET /api/aigateway/agent/worker/status`
  - `POST /api/aigateway/agent/run-queue/{id}/dead-letter`
- 新增 DTO：
  - `AgentRunQueueItemDto`
  - `AgentRunQueueSummaryDto`
  - `AgentWorkerStatusDto`
  - `AgentWorkerHeartbeatDto`
- 新增权限：
  - `AiGateway.RunQueue.Read`
  - `AiGateway.RunQueue.Manage`
  - `AiGateway.WorkerStatus.Read`
- 新增错误码：
  - `agent_worker_unavailable`
  - `agent_worker_workspace_mismatch`
  - `agent_run_queue_dead_letter_not_allowed`
  - `agent_run_queue_operation_denied`

## 修复点

- 初版 heartbeat 首次创建时同时 `Add` 和 `Update`，触发 EF `xmin` 并发异常，导致 DataWorker 轮询失败、queue item 停留在 `Queued`。已修复为新 heartbeat 只 Add，已有 heartbeat 才 Update。
- dead-letter 命令按现有审计边界只写审计请求，不在命令文件中显式调用 `auditLogWriter.SaveChangesAsync`，避免破坏现有审计保存规则。

## 验证命令

- `dotnet build AICopilot/src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj --no-restore`
  - 通过，0 warning，0 error。
- `dotnet test AICopilot/src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName!~FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload" --no-build`
  - 通过，487 passed。
- `dotnet test AICopilot/src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
  - 通过，44 passed。
- `dotnet test AICopilot/src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj --no-restore`
  - 通过，6 passed。
- `dotnet list AICopilot/src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj package --vulnerable --include-transitive`
  - 未发现易受攻击的包。

## 剩余风险

- 本批只提供后端运维接口，前端运维页未实现。
- Worker status 的 workspace mismatch 只做健康告警，不阻止新任务入队；后续前端/运维侧需要基于状态明确提示。
- 未做自动重放未知工具副作用，失败恢复仍按现有 retry 规则执行。
- 当前 `src/vues` dirty 内容仍为外部阻塞项，本批未处理。
