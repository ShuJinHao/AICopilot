# AICopilot 后端阶段记录 - CloudReadonlyProductionOperations 拆分第一批 - 2026-05-27

## 本批目标

- 只在 `AICopilot` 内继续收口 CloudReadonly P14 production operations 结构债。
- 将 `CloudReadonlyProductionOperations.cs` 从 1126 行拆成 contracts、store、service、handler 四类文件。
- 保持 P12/P13/P14 gate、emergency stop、ledger、GA readiness、audit save 语义、公开 DTO/API、数据库结构和部署配置不变。

## 实际改动类别

- `CloudReadonlyProductionOperations.cs` 收敛为 contracts 文件，保留 statuses、DTO、commands、queries 和 `IProductionPilotOperationsStore`。
- 新增 `ProductionPilotOperationsStores.cs`，承接 in-memory store、repository store 和 store DTO/entity 映射。
- 新增 `CloudReadonlyProductionOperationsService.cs`，承接 status、ledger、metrics、artifact refs backfill 和 GA readiness 计算。
- 新增 `CloudReadonlyProductionOperationsHandlers.cs`，承接 query/command handlers 与 audit write/save。
- 将 `SecurityHardeningTests` 的 explicit audit save 白名单从 contracts 文件调整到 handler 文件。

## 影响模块

- `src/services/AICopilot.AiGatewayService/CloudReadiness/CloudReadonlyProductionOperations.cs`
- `src/services/AICopilot.AiGatewayService/CloudReadiness/ProductionPilotOperationsStores.cs`
- `src/services/AICopilot.AiGatewayService/CloudReadiness/CloudReadonlyProductionOperationsService.cs`
- `src/services/AICopilot.AiGatewayService/CloudReadiness/CloudReadonlyProductionOperationsHandlers.cs`
- `src/tests/AICopilot.BackendTests/SecurityHardeningTests.cs`

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 DTO/API 语义、permission attributes、status 字符串、数据库迁移、容器编排、MCP 配置或部署配置。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~EnterpriseCloudReadonlyProductionOperationsP14Tests|FullyQualifiedName~EnterpriseProductionPilotHardeningP16Tests|FullyQualifiedName~SecurityHardeningTests"`：通过，69/69。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`：通过，759/759。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`：通过，44/44。

## 剩余风险

- 本批只拆 P14 production operations 文件结构，没有处理 MCP chiseled 镜像缺 `npx` 的部署风险。
- 本批未做 GitHub review/commit/PR 流程。
- `CloudReadiness` 下 `CloudReadonlyProductionPilot.cs`、`CloudReadonlyProductionControlledPilot.cs` 仍是 1000 行级结构债，后续可单独拆分。

## 下一阶段进入条件

- 可继续拆 `CloudReadonlyProductionPilot.cs` 或 `CloudReadonlyProductionControlledPilot.cs`，前提是仍只改 `AICopilot`，且不改变 P12/P13 gate、审批、只读边界和 audit payload。
- 或单独开启 MCP 独立容器部署批次，明确 MCP server 包、SSE endpoint、compose profile 和非破坏性配置步骤。
- 后续批次继续默认不创建分支、worktree、commit 或 PR，除非当前轮另有明确要求。
