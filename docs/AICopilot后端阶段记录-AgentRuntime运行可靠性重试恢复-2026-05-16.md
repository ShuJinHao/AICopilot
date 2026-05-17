# AICopilot 后端阶段记录：Agent Runtime 运行可靠性 + 重试恢复闭环

## 改动范围

- 仅修改 AICopilot 后端、后端测试和阶段记录。
- 未修改 `src/vues` 前端，未修改 Cloud/Edge。
- 当前 `src/vues` 仍有非本批次 dirty 内容，继续作为外部阻塞项记录。

## Schema / DTO 变化

- AiGateway fresh baseline 增加 `agent_task_run_attempts`。
- `agent_tasks` 增加 `active_run_attempt_id`、`run_attempt_count`、`run_lease_id`、`run_lease_owner`、`run_lease_expires_at`。
- `tool_execution_records` 增加 `run_attempt_id`。
- `AgentTaskDto` 兼容新增 `activeRunAttemptId`、`runAttemptCount`、`isRunInProgress`。
- `ToolExecutionRecordDto` 兼容新增 `runAttemptId`。
- 新增只读接口：`GET /api/aigateway/agent/task/{id}/run-attempts`。
- 新增重试接口：`POST /api/aigateway/agent/task/retry`。

## 运行锁与 Retry 行为

- `RunAgentTask` 开始执行前创建或恢复 `AgentTaskRunAttempt`，并获取 5 分钟 lease。
- 已存在未过期 lease 时返回 `agent_task_run_in_progress`，不重复执行 step，不重复写工具记录。
- Runtime 每个 step 前刷新 lease；工具 gate、schema recheck、执行失败和拒绝都会关联 active attempt。
- 等待 ToolCall / FinalOutput 审批时释放 lease，但保留 active attempt，审批后续跑复用同一 attempt。
- `RetryAgentTask` 仅允许 `Failed` 且 workspace 未 finalized 的任务；会取消旧 pending approval，保留 completed step，重置 failed/后续 step。
- `CancelAgentTask` 会取消 active attempt，并关闭 pending approval。

## 验证记录

- `dotnet build AICopilot/src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj --no-restore`：通过。
- `dotnet test AICopilot/src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "Suite=ToolRegistryGovernance"`：通过，33 个测试。
- `dotnet test AICopilot/src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName!~FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload" --no-build`：通过，478 个测试，耗时 5 分 44 秒。
- `dotnet test AICopilot/src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`：通过，44 个测试。
- `dotnet test AICopilot/src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj --no-restore`：通过，6 个测试。
- `dotnet list AICopilot/src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj package --vulnerable --include-transitive`：通过，未发现易受攻击的包。

## 剩余风险

- 本批不引入后台队列或分布式调度器；同步 run 仍依赖请求生命周期。
- lease 过期后的未知运行结果按失败保护处理，用户需要显式 retry。
- 完整 BackendTests 过滤命令仍需给足 5 分钟以上，前端源码断言过滤项继续保留。
