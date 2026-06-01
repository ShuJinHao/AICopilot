# AICopilot 后端阶段记录 - CloudReadonlyContracts 拆分第一批 - 2026-05-31

## 本批目标

- 只修改 `AICopilot` 后端 shared contracts。
- 拆分 `CloudReadonlyContracts.cs`，将 CloudReadonly options 与 marker 常量按能力边界拆成多个 contracts 文件。
- 保持所有 public 类型名、属性名、默认值、`SectionName`、`EnsureValid` 校验逻辑和异常文案不变。
- 不修改 CloudReadonly 只读边界、ToolRegistry 保护、approval、query/result wire shape、配置绑定、数据库迁移、部署编排或 MCP 配置。

## 实际改动类别

- 移除原 `CloudReadonlyContracts.cs` 单体文件。
- 新增基础 CloudReadonly mode、Simulation、Real options 文件。
- 新增 Sandbox、SandboxAgentTrial、SandboxControlledTrial options 文件。
- 新增 PilotReadiness options 文件。
- 新增 ProductionPilot 与 ProductionControlledPilot options 文件。
- 新增 CloudReadonly marker/source constants 文件。

## 影响模块

- `AICopilot/src/services/AICopilot.Services.Contracts/Contracts/`
- 影响能力：shared contracts 的文件组织。
- 不影响能力：CloudReadonly 配置语义、runtime 查询路径、审批策略、source marker 值、ToolRegistry 防护和外部 API/DTO。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改 Services 行为、Infrastructure、Host DI、数据库迁移、部署编排或 MCP 配置。
- 未新增 NuGet/npm/容器依赖。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

- `dotnet build src/services/AICopilot.Services.Contracts/AICopilot.Services.Contracts.csproj --no-restore`
  - 结果：通过，0 warning / 0 error。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~CloudReadonlySimulationTests|FullyQualifiedName~EnterpriseCloudReadonlyReadinessP5Tests|FullyQualifiedName~EnterpriseCloudReadonlySandboxP6Tests|FullyQualifiedName~EnterpriseCloudReadonlySandboxAgentTrialP7Tests|FullyQualifiedName~EnterpriseCloudReadonlySandboxExpansionP8Tests|FullyQualifiedName~EnterpriseCloudReadonlyPilotReadinessP11Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionPilotP12Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionControlledPilotP13Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionOperationsP14Tests|FullyQualifiedName~EnterpriseProductionPilotHardeningP16Tests|FullyQualifiedName~ToolRegistryGovernanceTests|FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~FrontendIntegrationContractTests"`
  - 结果：通过，186 passed / 0 failed。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
  - 结果：最终复跑通过，759 passed / 0 failed。首次全量运行出现 1 个 `AgentSimulationAcceptanceTests.OfflineSimulation_ShouldKeepProductionCloudReadonlyToolProtected` 登录 500；单测复跑通过 1/1，随后全量复跑通过 759/759，判定为集成宿主瞬时失败，非本批 contracts 拆分缺陷。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
  - 结果：通过，44 passed / 0 failed。
- `git diff --check`
  - 结果：通过，无 whitespace error。
- `git diff --name-only`
  - 结果：仅列出当前 AICopilot 工作树内 tracked diff；工作树仍包含前序多批 AICopilot 拆分改动。本批新增 CloudReadonly contracts 拆分文件和本阶段记录为 untracked 文件，需结合 `git status --short --untracked-files=all` 审查。
- `wc -l src/services/AICopilot.Services.Contracts/Contracts/*CloudReadonly*.cs`
  - 结果：`CloudReadonlyMarkers.cs` 52 行，`CloudReadonlyOptions.cs` 76 行，`CloudReadonlyPilotReadinessOptions.cs` 75 行，`CloudReadonlyProductionPilotOptions.cs` 203 行，`CloudReadonlySandboxOptions.cs` 189 行，单文件均低于 500 行。

## 剩余风险

- 本批是 contracts 文件组织拆分，未改变公开契约语义。
- 当前工作树包含前序多批 AICopilot 拆分改动，本批只追加 CloudReadonly contracts 拆分。
- 真实运行行为已由 BackendTests 全量复跑覆盖；本批不新增配置项或部署演练。

## 下一阶段进入条件

- `AICopilot.BackendTests` 与 `ArchitectureTests` 已保持绿色，可进入下一批。
- 后续可继续评估 `HttpApi/DependencyInjection.cs`、`PilotAuthorizationSubmission.cs`、`SemanticSummaryProfiles.cs` 或 `McpServerBootstrap.cs` 等剩余大文件。
- 如果后续需要改变 option 字段、marker 值、section name、异常文案、配置绑定、CloudReadonly 行为、公开接口、数据库结构或部署配置，必须单独开批并重新确认范围。
