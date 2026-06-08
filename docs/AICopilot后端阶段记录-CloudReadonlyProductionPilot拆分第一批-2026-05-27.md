# AICopilot 后端阶段记录 - CloudReadonlyProductionPilot 拆分第一批 - 2026-05-27

## 本批目标

- 只改 `AICopilot`。
- 将 `CloudReadonlyProductionPilot.cs` 从 P12 production pilot 单体实现收敛为 contracts/status/DTO/command/query/store interface 声明文件。
- 按 P12 production pilot 的 contracts、store、service、handlers 边界拆分实现。
- 保持 P11/P12/P13/P14 gate、ToolRegistry 保护、audit save、window/scenario/query result 语义不变。

## 实际改动类别

- `CloudReadonlyProductionPilot.cs`
  - 保留 P12 statuses、DTO、commands、queries、store interface。
  - 移除 handler、store、service 实现代码。
- `CloudReadonlyProductionPilotStores.cs`
  - 承接 `InMemoryCloudReadonlyProductionPilotStore`。
  - 承接 `RepositoryCloudReadonlyProductionPilotStore`。
  - 保持 repository save、run ledger DTO 映射、20 条 run 保留口径不变。
- `CloudReadonlyProductionPilotService.cs`
  - 承接 P12 window 创建、状态更新、gate/status 计算、scenario 执行编排。
  - 保持 CloudReadonly boundary、ToolRegistry protected tool 校验、emergency stop、CloudAiRead 调用和 P14 ledger backfill 语义不变。
- `CloudReadonlyProductionPilotHandlers.cs`
  - 承接 P12 query/command handlers。
  - 保持原显式 audit write/save 位置和 payload 字段不变。
- `CloudReadonlyProductionPilotScenarioCatalog.cs`
  - 承接固定 scenario/endpoint catalog、endpoint allowlist 过滤、query payload 构建、artifact type 过滤和 CloudAiRead response row projection。
  - 该 helper 是为满足本批单文件低于 500 行的验收口径而从 service 边界内继续拆出的 internal 实现，不新增公开 API。
- `SecurityHardeningTests.cs`
  - 将 explicit audit save 白名单从旧单体文件更新为 `CloudReadonlyProductionPilotHandlers.cs`。

## 影响模块

- `AICopilot.AiGatewayService/CloudReadiness`
  - P12 production pilot 内部结构。
- `AICopilot.BackendTests`
  - 安全扫描测试中的 P12 explicit audit save 文件路径。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 contracts/DTO 字段语义、数据库迁移、部署编排、MCP 配置或容器配置。
- 未修改 P12/P13/P14/P16 业务状态字符串、审批 gate、ToolRegistry 保护策略、CloudReadonly 只读防护或 audit payload 语义。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~EnterpriseCloudReadonlyProductionPilotP12Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionControlledPilotP13Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionOperationsP14Tests|FullyQualifiedName~EnterpriseProductionPilotHardeningP16Tests|FullyQualifiedName~SecurityHardeningTests"`
  - 结果：通过 87/87。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
  - 结果：通过 759/759。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
  - 结果：通过 44/44。
- `git diff --check`
  - 结果：通过，无 whitespace/error 输出。
- `wc -l src/services/AICopilot.AiGatewayService/CloudReadiness/*ProductionPilot*.cs`
  - `CloudReadonlyProductionPilot.cs`：136 行。
  - `CloudReadonlyProductionPilotHandlers.cs`：171 行。
  - `CloudReadonlyProductionPilotScenarioCatalog.cs`：218 行。
  - `CloudReadonlyProductionPilotService.cs`：461 行。
  - `CloudReadonlyProductionPilotStores.cs`：257 行。
  - `ProductionPilotOperationsStores.cs`：433 行。

## 剩余风险

- P13 `CloudReadonlyProductionControlledPilot.cs` 仍是较集中的 controlled pilot 文件，可作为下一批按 controlled pilot contracts/service/handlers/store 或 helper 边界拆分。
- MCP 独立容器部署仍未处理，保持为后续部署批次。
- 本批是结构拆分，不改变 CloudAiRead 真实接口可用性；真实环境仍依赖 CloudAiRead 配置、Cloud 只读接口与部署网络状态。

## 下一阶段进入条件

- 继续从当前绿色基线进入下一批。
- 优先候选：`CloudReadonlyProductionControlledPilot.cs` 拆分第一批。
- 进入前继续执行 `docs/总规则.md`、`AICopilot/AGENTS.md` 和 `AICopilot/资料/AICopilot业务规则.md` 门禁。
