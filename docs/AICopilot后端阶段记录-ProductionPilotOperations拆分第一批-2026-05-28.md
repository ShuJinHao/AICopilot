# AICopilot 后端阶段记录 - ProductionPilotOperations 拆分第一批 - 2026-05-28

## 本批目标

- 只修改 `AICopilot` 后端 Core 层。
- 拆分 `ProductionPilotOperations.cs`，将 880 行多实体单体文件收敛为按实体命名的独立文件。
- 保持 production pilot 相关 public 类型、构造参数、属性、方法和 normalize 行为不变。
- 不改变 EF mapping、repository、service、handler、store、数据库结构、部署编排或 MCP 配置。

## 实际改动类别

- 移除原 `ProductionPilotOperations.cs` 单体文件。
- 新增 `ProductionPilotEmergencyStopState.cs`，保留 emergency stop state 与原 `NormalizeRequired/NormalizeOptional/NormalizeStrings/NormalizeGuids` internal static helper。
- 新增 P12/P13/P14/P16 生产试点实体独立文件：incident、run ledger、pilot window、fixed pilot run、controlled intent、controlled run、GA readiness assessment。
- 所有新文件保持原 namespace：`AICopilot.Core.AiGateway.Aggregates.ProductionOperations`。

## 影响模块

- `AICopilot/src/core/AICopilot.Core.AiGateway/Aggregates/ProductionOperations/`
- 影响能力：Core 层 production pilot 聚合实体文件组织。
- 不影响能力：CloudReadonly production pilot 状态机、emergency stop、ledger retention、GA readiness、审批、audit、EF 配置和数据库迁移。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改 `AICopilot` Services 行为。
- 未修改 Infrastructure EF 配置、数据库迁移、容器编排、MCP 配置。
- 未新增 NuGet/npm/容器依赖。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~EnterpriseCloudReadonlyProductionPilotP12Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionControlledPilotP13Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionOperationsP14Tests|FullyQualifiedName~EnterpriseProductionPilotHardeningP16Tests|FullyQualifiedName~AgentRunQueueProductionOpsTests"`
  - 结果：通过 36/36。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
  - 结果：通过 759/759。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
  - 结果：通过 44/44。
- `git diff --check`
  - 结果：通过，无 whitespace error 输出。
- `git diff --name-only`
  - 结果：当前工作树包含前序多批 AICopilot 拆分改动；本批实际新增/删除文件限定在 ProductionOperations 聚合拆分文件与本阶段记录。
- `wc -l src/core/AICopilot.Core.AiGateway/Aggregates/ProductionOperations/*.cs`
  - 所有 ProductionOperations 实体文件均低于 500 行。

## 剩余风险

- 本批是纯文件组织拆分，未新增业务能力。
- 未拆 `ProductionOperationConfiguration.cs`，因此 EF mapping 仍在原位置集中维护。
- 未执行真实生产试点外部联调；本批不涉及外部协议或数据库结构变化。

## 下一阶段进入条件

- 继续从 `AICopilot.BackendTests 759/759` 与 `ArchitectureTests 44/44` 绿色基线进入下一批。
- 下一批可优先评估仍超过 500 行且边界清晰的 AICopilot 后端文件，例如 contracts、DI、EF configuration 或其他 Core 聚合。
- 如果后续需要改变 EF 配置、实体字段、数据库结构、生产试点 gate、emergency stop、ledger、audit payload 或公开接口，必须单独开批并重新确认范围。
