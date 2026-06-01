# AICopilot 后端阶段记录 - CloudReadonlySimulation 拆分第一批 - 2026-05-27

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `CloudReadonlySimulation.cs`，把 simulation intent、query parsing、query execution、seed dataset 从单体文件中剥离。
- 保持 `SimulationCloudReadonlyDataProvider`、`CloudReadonlySimulationDataSet`、`CloudReadonlySimulationIntentPlanner` 的名称、可见性、构造语义和运行行为不变。
- 不新增 simulation 场景，不修 MCP 部署，不触碰 Cloud/Edge/UI/数据库迁移/部署编排。

## 实际改动

- `CloudReadonlySimulation.cs` 收敛为 `SimulationCloudReadonlyDataProvider` 薄入口，继续负责 `Simulation` 模式开关检查、query parse 调用和 provider contract 返回。
- 新增 `CloudReadonlySimulationIntentPlanner.cs`，承接 intent、target、kind 推导和 simulation plan intent 组装。
- 新增 `CloudReadonlySimulationQuery.cs`，承接 JSON/free-text query parser，以及 line/days/limit/level/shift/status 等提取逻辑。
- 新增 `CloudReadonlySimulationQueryExecutor.cs`，承接 devices、logs、capacity、quality、work orders、weekly report 六类 simulation 查询执行、row projection、limit、summary 和 source metadata 组装。
- 新增 `CloudReadonlySimulationDataSet.cs`，承接 simulation seed dataset builders 和 simulation record types。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.AiGatewayService/AgentTasks`
- 能力边界：CloudReadonly simulation provider 内部结构拆分。
- 公开契约：未修改 `ICloudReadonlySimulationIntentPlanner`、`ICloudReadonlyDataProvider`、`CloudReadonlyAgentToolRequest/Result`、`CloudReadonlyOptions` 或 contracts。
- 运行行为：未修改 Simulation source mode/source label/isSimulation markers、supported intent code、query row field name、summary、limit/truncation、date range 和 seed count 语义。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改数据库迁移、部署编排、MCP 配置、公开 DTO/API 语义。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~CloudReadonlySimulationTests|FullyQualifiedName~AgentSimulationAcceptanceTests|FullyQualifiedName~EnterpriseDataGovernanceP0Tests|FullyQualifiedName~ToolRegistryGovernanceTests|FullyQualifiedName~EnterpriseDynamicPlannerP3Tests|FullyQualifiedName~SecurityHardeningTests"
```

- 结果：通过 123 / 123，失败 0，跳过 0。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 41 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0。

```bash
zsh -f -c 'setopt NULL_GLOB; wc -l src/services/AICopilot.AiGatewayService/AgentTasks/*CloudReadonlySimulation*.cs src/services/AICopilot.AiGatewayService/AgentTasks/Simulation*.cs'
```

- `CloudReadonlySimulation.cs`：29 行。
- `CloudReadonlySimulationDataSet.cs`：181 行。
- `CloudReadonlySimulationIntentPlanner.cs`：71 行。
- `CloudReadonlySimulationQuery.cs`：174 行。
- `CloudReadonlySimulationQueryExecutor.cs`：281 行。

## 剩余风险

- 本批只处理 `CloudReadonlySimulation.cs`，没有处理 `AgentReportComposer.cs`、`AgentApprovalManagement.cs`、`AgentDynamicPlanner.cs` 等后续结构债。
- `CloudReadonlySimulationQueryExecutor.cs` 当前 281 行，低于 500 行；后续如果新增 simulation 查询类型，应优先按查询域继续拆分 executor，避免重新膨胀。
- 本批保持 simulation dataset 为内存 seed 数据，没有引入外部数据源或部署配置变更。

## 下一阶段进入条件

- 从当前绿色基线继续：`BackendTests 759/759`、`ArchitectureTests 44/44`。
- 下一批建议继续在 `AgentTasks` 内处理 `AgentReportComposer.cs` 或 `AgentApprovalManagement.cs`；如果转向动态规划，则单独规划 `AgentDynamicPlanner.cs`。
- 如果下一批涉及 ToolRegistry、CloudReadonly 只读边界或安全源码扫描断言，只允许迁移扫描路径，不允许放松安全断言。
