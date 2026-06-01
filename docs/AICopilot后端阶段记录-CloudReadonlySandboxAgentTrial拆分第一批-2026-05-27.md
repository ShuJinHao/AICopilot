# AICopilot 后端阶段记录 - CloudReadonlySandboxAgentTrial 拆分第一批 - 2026-05-27

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `CloudReadonlySandboxAgentTrial.cs`，降低 P7 fixed sandbox trial 单文件职责密度。
- 保持 P5/P6 smoke gate、P7 固定场景、P8 controlled trial 依赖、ToolRegistry 保护、返回 JSON、hash、source metadata 和错误语义不变。
- 不新增功能，不改 MCP 部署，不触碰 Cloud/Edge/UI/数据库迁移/部署编排。

## 实际改动

- `CloudReadonlySandboxAgentTrial.cs` 收敛为 contracts 文件，仅保留 statuses、DTO、commands、queries 和 history store interface。
- 新增 `CloudReadonlySandboxAgentTrialHandlers.cs`，承接 status query 和 run command handlers。
- 新增 `CloudReadonlySandboxAgentTrialStores.cs`，承接 `InMemoryCloudReadonlySandboxAgentTrialHistoryStore`，保留 20 条 history 保留口径。
- 新增 `CloudReadonlySandboxAgentTrialService.cs`，承接 `BuildStatus`、`RunScenarioAsync` 主编排。
- 新增 `CloudReadonlySandboxAgentTrialScenarioCatalog.cs`，承接 6 个固定场景、endpoint allowlist/blocklist 和 scenario 查询。
- 新增 `CloudReadonlySandboxAgentTrialQueryProjection.cs`，承接 query payload、CloudReadonlySandbox JSON rows 提取、source metadata projection 和 hash helper。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.AiGatewayService/CloudReadiness`
- 能力边界：P7 CloudReadonly sandbox agent fixed trial 内部结构拆分。
- 公开契约：未修改 DTO 字段、query/command 类型、permission attributes、status 字符串、history store interface、service 构造参数和 public/static 方法签名。
- 运行行为：未修改 P5/P6 smoke gate、P7 fixed scenario allowlist、P8 controlled trial 入口、ToolRegistry data boundary、返回 JSON、source metadata、hash 和错误映射口径。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改数据库迁移、部署编排、MCP 配置、公开 DTO/API 语义。
- 未创建分支、worktree、commit 或 PR。
- 未修改 `SecurityHardeningTests`；本批没有 explicit audit save 白名单迁移需求。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~EnterpriseCloudReadonlyReadinessP5Tests|FullyQualifiedName~EnterpriseCloudReadonlySandboxP6Tests|FullyQualifiedName~EnterpriseCloudReadonlySandboxAgentTrialP7Tests|FullyQualifiedName~EnterpriseCloudReadonlySandboxExpansionP8Tests|FullyQualifiedName~ToolRegistryGovernanceTests|FullyQualifiedName~SecurityHardeningTests"
```

- 结果：通过 148 / 148，失败 0，跳过 0。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 38 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0。

```bash
wc -l src/services/AICopilot.AiGatewayService/CloudReadiness/*SandboxAgentTrial*.cs
```

- `CloudReadonlySandboxAgentTrial.cs`：73 行。
- `CloudReadonlySandboxAgentTrialHandlers.cs`：52 行。
- `CloudReadonlySandboxAgentTrialQueryProjection.cs`：140 行。
- `CloudReadonlySandboxAgentTrialScenarioCatalog.cs`：108 行。
- `CloudReadonlySandboxAgentTrialService.cs`：214 行。
- `CloudReadonlySandboxAgentTrialStores.cs`：28 行。

## 剩余风险

- 本批只做 P7 fixed sandbox trial 结构拆分，没有处理 MCP 独立容器、部署配置修复或 Runtime 进一步拆分。
- `CloudReadonlyProductionPilotService.cs` 与 `CloudReadonlyProductionControlledPilotService.cs` 仍接近 500 行，但当前均低于警戒线，未在本批扩大处理。

## 下一阶段进入条件

- 从当前绿色基线继续：`BackendTests 759/759`、`ArchitectureTests 44/44`。
- 下一批建议先复核 `CloudReadiness` 剩余接近 500 行的 production pilot service，确认是否需要进一步按 policy/query/projection/ledger 边界拆分。
- 若进入 MCP 部署修复，必须单独规划 MCP server 包、SSE endpoint、compose profile 和非破坏性配置迁移步骤。
