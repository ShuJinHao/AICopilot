# AICopilot 后端阶段记录 - CloudReadonlySandboxControlledTrial 拆分第一批 - 2026-05-27

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `CloudReadonlySandboxControlledTrial.cs`，降低 P8 controlled sandbox trial 单文件职责密度。
- 保持 P5/P6 sandbox readiness、P7 fixed sandbox trial、P8 controlled sandbox trial、ToolRegistry 保护、intent/plan/run/query result 语义不变。
- 不新增功能，不改 MCP 部署，不触碰 Cloud/Edge/UI/数据库迁移/部署编排。

## 实际改动

- `CloudReadonlySandboxControlledTrial.cs` 收敛为 contracts 文件，仅保留状态常量、DTO、commands、queries 和 intent store interface。
- 新增 `CloudReadonlySandboxControlledTrialHandlers.cs`，承接 status query 和 create plan command handlers。
- 新增 `CloudReadonlySandboxControlledTrialStores.cs`，承接 `InMemoryCloudReadonlySandboxControlledTrialIntentStore`，保留 100 条 intent 保留口径。
- 新增 `CloudReadonlySandboxControlledTrialService.cs`，承接 BuildStatus、CreateIntent、ValidateIntentForPlan、RunIntentAsync 主编排。
- 新增 `CloudReadonlySandboxControlledTrialGoalPolicy.cs`，承接 goal normalize、blocked terms、endpoint allowlist、analysis type、artifact/time range 归一化。
- 新增 `CloudReadonlySandboxControlledTrialQueryProjection.cs`，承接 sandbox query payload、JSON rows 提取和 sandbox source metadata projection。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.AiGatewayService/CloudReadiness`
- 能力边界：CloudReadonly sandbox controlled trial 内部结构拆分。
- 公开契约：未修改 DTO 字段、command/query 类型、status 字符串、marker 常量、intent store interface、service 构造参数和 public 方法签名。
- 运行行为：未修改 P8 gate、ToolRegistry data boundary、approval 语义、query payload、返回 JSON metadata 和 intent store 保留策略。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改数据库迁移、部署编排、MCP 配置、公开 DTO/API 语义。
- 未修改 `SecurityHardeningTests` 白名单；本批没有 explicit audit save 迁移需求。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~EnterpriseCloudReadonlySandboxExpansionP8Tests|FullyQualifiedName~EnterpriseCloudReadonlySandboxAgentTrialP7Tests|FullyQualifiedName~EnterpriseCloudReadonlyReadinessP5Tests|FullyQualifiedName~EnterpriseCloudReadonlySandboxP6Tests|FullyQualifiedName~ToolRegistryGovernanceTests|FullyQualifiedName~SecurityHardeningTests"
```

- 结果：通过 148 / 148，失败 0，跳过 0。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 27 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0。

```bash
wc -l src/services/AICopilot.AiGatewayService/CloudReadiness/*SandboxControlledTrial*.cs
```

- `CloudReadonlySandboxControlledTrial.cs`：74 行。
- `CloudReadonlySandboxControlledTrialGoalPolicy.cs`：182 行。
- `CloudReadonlySandboxControlledTrialHandlers.cs`：62 行。
- `CloudReadonlySandboxControlledTrialQueryProjection.cs`：119 行。
- `CloudReadonlySandboxControlledTrialService.cs`：364 行。
- `CloudReadonlySandboxControlledTrialStores.cs`：33 行。

## 剩余风险

- 本批只做 P8 controlled sandbox trial 结构拆分，没有处理仍超过 500 行的其他 CloudReadiness 文件。
- `CloudReadonlyPilotReadiness.cs`、`CloudReadonlyReadiness.cs`、`CloudReadonlySandboxAgentTrial.cs` 仍是后续结构债候选。
- MCP 独立容器、部署配置修复和 Runtime 进一步拆分不在本批范围内。

## 下一阶段进入条件

- 从当前绿色基线继续：`BackendTests 759/759`、`ArchitectureTests 44/44`。
- 下一批建议优先拆 `CloudReadonlyPilotReadiness.cs` 或 `CloudReadonlyReadiness.cs`，按 contracts/store/service/handlers/policy 的边界继续收敛。
- 若进入 MCP 部署修复，必须单独规划 MCP server 包、SSE endpoint、compose profile 和非破坏性配置迁移步骤。
