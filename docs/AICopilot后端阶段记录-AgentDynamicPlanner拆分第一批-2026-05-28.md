# AICopilot 后端阶段记录 - AgentDynamicPlanner 拆分第一批 - 2026-05-28

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `AgentDynamicPlanner.cs`，把 planner prompt、planner input payload、response parser 和 limits 从单体 planner 中剥离。
- 保持 `AgentDynamicPlannerRequest`、`AgentPlannerDataSourceSummary`、`IAgentDynamicPlanner`、`DefaultAgentDynamicPlanner` 的公开入口和构造契约不变。
- 不新增动态规划能力，不改 ToolRegistry data boundary，不改 RAG 权限复核，不改模型调用行为，不触碰 Cloud/Edge/UI/数据库迁移/部署编排/MCP 配置。

## 实际改动

- `AgentDynamicPlanner.cs` 收敛为公开契约和 `DefaultAgentDynamicPlanner` 薄编排层。
- 新增 `AgentDynamicPlannerPromptComposer.cs`，承接 planner system instruction 组合。
- 新增 `AgentDynamicPlannerInputBuilder.cs`，承接 planner input payload 构建、tool schema fallback、字段脱敏和长度裁剪。
- 新增 `AgentDynamicPlannerResponseParser.cs`，承接 structured/text response 解析、root/step 字段白名单、stepType/inputJson 校验和错误映射。
- 新增 `AgentDynamicPlannerLimits.cs`，集中 `MaxDynamicSteps`、`MaxPlannerResponseTextLength`、`MaxStepInputJsonLength`。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.AiGatewayService/AgentTasks`
- 能力边界：动态 planner 内部结构拆分。
- 公开契约：未修改 `IAgentDynamicPlanner.CreatePlanAsync`、`DefaultAgentDynamicPlanner` 类型名/构造参数/public 方法、`AgentDynamicPlannerRequest` 字段或 `AgentPlannerDataSourceSummary` 字段。
- 运行行为：未修改 planner prompt 文案、JSON-only 约束、allowed fields、错误消息、脱敏规则、最大步骤数、inputJson 长度限制或 `AppProblemCodes`。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 DTO/API 语义、数据库迁移、部署编排或 MCP 配置。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~DynamicPlannerContractTests|FullyQualifiedName~EnterpriseDynamicPlannerP3Tests|FullyQualifiedName~ToolRegistryGovernanceTests|FullyQualifiedName~SecurityHardeningTests"
```

- 结果：通过 117 / 117，失败 0，跳过 0，耗时 525 ms。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 39 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0，耗时 417 ms。

```bash
wc -l src/services/AICopilot.AiGatewayService/AgentTasks/*DynamicPlanner*.cs
```

- `AgentDynamicPlanner.cs`：128 行。
- `AgentDynamicPlannerInputBuilder.cs`：128 行。
- `AgentDynamicPlannerLimits.cs`：8 行。
- `AgentDynamicPlannerPromptComposer.cs`：17 行。
- `AgentDynamicPlannerResponseParser.cs`：250 行。

## 剩余风险

- 本批只处理 `AgentDynamicPlanner.cs`，没有处理 `AgentTaskAuditQueries.cs`、`AgentRunQueueOperations.cs`、`AgentTrialScenarios.cs` 等接近 500 行的文件。
- `AgentDynamicPlannerResponseParser.cs` 当前 250 行，低于 500 行；后续如果新增 planner 输出字段，应优先扩展 parser 的白名单和校验测试，避免把解析逻辑堆回默认 planner。
- 由于本批保持错误消息和 contract 不变，未新增额外行为测试。

## 下一阶段进入条件

- 从当前绿色基线继续：`BackendTests 759/759`、`ArchitectureTests 44/44`。
- 下一批建议先做一次 `AgentTasks` 结构债复盘，确认是否继续处理 450-500 行区间文件，或切到全仓更大的非 AgentTasks 文件。
- 如果下一批涉及 planner 输出契约、ToolRegistry 校验、CloudReadonly 只读边界或公开接口，只允许先出单独计划，不允许顺手改变语义。
