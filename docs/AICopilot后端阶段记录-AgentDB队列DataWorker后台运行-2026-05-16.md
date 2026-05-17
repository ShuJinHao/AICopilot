# AICopilot 后端阶段记录：Agent DB 队列 + DataWorker 后台运行

日期：2026-05-16

## 改动范围

- 只修改 `AICopilot` 后端、后端测试和本阶段记录。
- 未修改 `AICopilot/src/vues`。
- 未修改 `IIoT.CloudPlatform`、`IIoT.EdgeClient`。

## 完成内容

- 新增 `AgentTaskRunQueueItem`，使用 AiGateway DB 表承载 Agent run/retry/approval resume 队列。
- `RunAgentTask` 改为只入队 `Manual` trigger，不再在 HTTP 请求内执行 runtime。
- `RetryAgentTask` 保留失败 step 之前已完成结果，重置失败及后续 step，取消旧 pending approval 后入队 `Retry` trigger。
- ToolCall 审批通过后自动入队 `ApprovalResume`，FinalOutput 仍由 workspace finalize 显式确认。
- `CancelAgentTask` 同时取消 active queue item、active attempt 和 pending approval。
- `AICopilot.DataWorker` 接入 `AgentTaskRunQueueWorker`，负责领取队列、执行 runtime、处理已开始但 lease 过期的保守失败。

## Schema / DTO / 接口

- AiGateway fresh baseline 增加 `agent_task_run_queue_items`。
- `AgentTaskDto` 兼容新增：
  - `queuedRunId`
  - `runQueueStatus`
  - `isRunQueued`
- 新增只读接口：
  - `GET /api/aigateway/agent/task/{id}/run-queue`
- 新增错误码：
  - `agent_task_run_queued`
  - `agent_task_run_queue_not_found`
  - `agent_task_run_queue_lease_expired`

## 运行策略

- 同一 task 同时只允许一个 `Queued/Leased` queue item。
- Worker 领取后写 queue lease；runtime 继续使用既有 attempt/lease 保护。
- 如果 queue item 已开始执行且 lease 过期，按未知工具副作用处理：标记 queue/task/attempt failed，要求用户显式 retry，不自动重放。
- Tool Registry、schema recheck、MCP、CloudReadonly、approval、ToolExecutionRecord 脱敏规则保持不放松。

## 验证记录

- 已通过：`dotnet build AICopilot/src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj --no-restore`
- 已通过：`dotnet build AICopilot/src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
- 已通过：`dotnet test AICopilot/src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName!~FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload" --no-build`
  - 结果：482 passed，0 failed，耗时 5 m 18 s。
- 已通过：`dotnet test AICopilot/src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
  - 结果：44 passed，0 failed。
- 已通过：`dotnet test AICopilot/src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj --no-restore`
  - 结果：6 passed，0 failed。
- 已通过：`dotnet list AICopilot/src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj package --vulnerable --include-transitive`
  - 结果：未发现易受攻击的包。

## 剩余风险

- DataWorker 现在承载 Agent runtime，部署时必须确保 `data-worker` 与 HttpApi 使用同一 AiGateway 数据库和文件 workspace 配置。
- 当前仍不引入分布式消息队列；高并发 worker 依赖 DB row version 与 active queue 唯一索引控制重复领取。
- `src/vues` dirty 内容仍为外部阻塞项，本批不处理。
