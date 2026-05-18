# A助理 Agent Runtime 队列生产运维增强 Batch 10 验收报告

- 批次：Batch 10 队列生产运维增强
- 范围：AICopilot 后端、后端测试、验收文档
- 日期：2026-05-18
- Cloud/Edge 触碰：否
- 前端 `src/vues` 触碰：否
- 真实 Cloud 访问引入：否
- shell 能力引入：否
- 任意服务器路径写入引入：否
- 新增依赖：否
- 数据库迁移：否

## 实际改动

- 新增 `AgentRunQueueOptions`，集中配置队列 lease、worker heartbeat 活跃窗口、retry 次数、指数退避上限、stale lease 处理策略。
- 扩展 run queue summary，新增 `averageWaitMs`、`averageRunMs`、`oldestQueuedWaitMs`、`workspaceMismatchCount`。
- worker status 使用配置化 heartbeat 活跃窗口，workspace mismatch 只按 active worker 统计。
- DataWorker 对 stale started lease 采用保守失败策略，不自动重跑，并写入运维审计。
- retry 增加最大次数限制和 30/60/120 秒指数退避，超过限制返回可诊断错误。
- cancel 保持幂等，terminal task 不重复产生取消副作用。
- run attempt terminal 状态后禁止再次完成，避免重复 succeeded/failed/cancelled。
- dead-letter、retry、cancel、stale lease fail 的审计 metadata 补齐 queue item、task、attempt、trigger、状态变化、failure code、retry attempt、availableAt。
- 新增 Batch 10 后端测试覆盖队列指标、worker 状态、stale lease、retry、cancel、terminal attempt、权限边界。

## 配置默认值

```json
{
  "AgentRunQueue": {
    "LeaseDurationSeconds": 300,
    "HeartbeatActiveWindowSeconds": 30,
    "MaxRetryAttempts": 3,
    "RetryBackoffSeconds": 30,
    "MaxRetryBackoffSeconds": 300,
    "StaleLeaseAction": "Fail"
  }
}
```

## 公共接口影响

- 保持既有路由不变：
  - `GET /api/aigateway/agent/run-queue`
  - `GET /api/aigateway/agent/run-queue/summary`
  - `GET /api/aigateway/agent/worker/status`
  - `POST /api/aigateway/agent/run-queue/{id}/dead-letter`
- 仅扩展 `AgentRunQueueSummaryDto` 字段，不改变既有字段语义。
- 不新增数据库字段；新增指标从已有 queue item、run attempt、worker heartbeat 字段计算。

## 验证结果

- PASS：`dotnet build .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug`
- PASS：`dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=Batch10RunQueueOps"`，8/8
- PASS：`dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=ToolRegistryGovernance"`，42/42
- PASS：`dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=Batch9McpToolGovernance"`，4/4
- PASS：`dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=Batch5ApprovalHardening|Suite=Batch6SecretProtection|Suite=Batch7ReportArtifacts|Suite=Batch8ArtifactVersioning"`，15/15
- PASS：`dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=AgentSimulationAcceptance"`，4/4
- PASS：`.\scripts\Run-AgentSimulationAcceptance.ps1`

## 边界确认

- 未修改 `IIoT.CloudPlatform`。
- 未修改 `IIoT.EdgeClient`。
- 未修改前端 `src/vues` dirty 文件。
- 未引入真实 Cloud 访问。
- 未引入 shell 工具能力。
- 未引入任意服务器路径写入入口。
- 未新增 NuGet/npm 依赖。
- 未新增数据库迁移。

## 剩余风险

- 本批只提供后端可查询指标和审计，不包含外部告警推送。
- stale lease 采用保守失败策略，后续若要自动恢复，需要单独设计去重和产物幂等边界。
