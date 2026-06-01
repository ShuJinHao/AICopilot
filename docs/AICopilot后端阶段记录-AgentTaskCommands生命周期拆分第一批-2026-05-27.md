# AICopilot 后端阶段记录 - AgentTaskCommands 生命周期拆分第一批 - 2026-05-27

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `AgentTaskCommands.cs` 中低风险的生命周期命令 handler 和 task loader。
- 保持 `PlanAgentTaskCommandHandler`、动态规划、RAG 权限复核、ToolRegistry guard、artifact/source metadata 语义不变。
- 不新增功能，不改 MCP 部署，不触碰 Cloud/Edge/UI/数据库迁移/部署编排。

## 实际改动

- `AgentTaskCommands.cs` 保留 command records、`AgentPlannerMode`、`PlanAgentTaskCommandHandler` 和计划生成 helper。
- 新增 `AgentTaskLifecycleCommandHandlers.cs`，承接 approve/run/retry/cancel 四个生命周期 command handler。
- 新增 `AgentTaskCommandLoader.cs`，承接 `AgentTaskCommandLoader.LoadTaskAsync`，继续供 command、query 和 audit query 复用。
- 未调整源码扫描测试断言；`SecurityHardeningTests` 仍读取 `AgentTaskCommands.cs` 验证 RAG 权限复核。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.AiGatewayService/AgentTasks`
- 能力边界：Agent task 命令处理内部结构拆分。
- 公开契约：未修改 command/query/DTO 字段、permission attributes、status 字符串、handler 构造参数和 public 方法签名。
- 运行行为：未修改 approve/run/retry/cancel 状态机、queue 行为、retry backoff、pending approval cancel、audit payload 或 DTO 映射。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改数据库迁移、部署编排、MCP 配置、公开 DTO/API 语义。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~ToolRegistryGovernanceTests|FullyQualifiedName~AgentRunQueueProductionOpsTests|FullyQualifiedName~EnterpriseDynamicPlannerP3Tests|FullyQualifiedName~SecurityHardeningTests"
```

- 结果：通过 117 / 117，失败 0，跳过 0。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 43 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0。

```bash
wc -l src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskCommands.cs src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskCommandLoader.cs src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskLifecycleCommandHandlers.cs
```

- `AgentTaskCommands.cs`：1091 行。
- `AgentTaskCommandLoader.cs`：35 行。
- `AgentTaskLifecycleCommandHandlers.cs`：334 行。

```bash
git diff --check
```

- 结果：通过，无 whitespace error。

```bash
git diff --name-only && git ls-files --others --exclude-standard
```

- 结果：已执行；当前 dirty worktree 中存在前序批次的 AICopilot 改动，本批新增/修改范围为 `AgentTaskCommands.cs`、`AgentTaskCommandLoader.cs`、`AgentTaskLifecycleCommandHandlers.cs` 和本阶段记录文档。

## 剩余风险

- 本批只拆生命周期命令和 loader，没有拆 `PlanAgentTaskCommandHandler` 内部 planner/helper。
- `AgentTaskCommands.cs` 仍超过 500 行，但大部分剩余体量集中在计划生成、动态规划、RAG 权限复核和 trial/simulation 计划组装，需要下一批单独拆分。
- 本批未处理 `CloudReadonlySimulation.cs`、`AgentReportComposer.cs`、`AgentApprovalManagement.cs` 等后续结构债。

## 下一阶段进入条件

- 从当前绿色基线继续：`BackendTests 759/759`、`ArchitectureTests 44/44`。
- 下一批建议继续处理 `AgentTaskCommands.cs`，优先拆出计划输入验证、trial/simulation plan builder 或 artifact/source metadata helper。
- 如果下一批涉及 `PlanAgentTaskCommandHandler` 的 RAG 权限源码扫描断言，只允许迁移扫描路径，不允许放松权限断言。
