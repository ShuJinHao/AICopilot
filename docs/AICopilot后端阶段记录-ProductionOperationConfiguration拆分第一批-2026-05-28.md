# AICopilot 后端阶段记录 - ProductionOperationConfiguration 拆分第一批 - 2026-05-28

## 本批目标

- 只修改 `AICopilot` 后端 Infrastructure EF 配置层。
- 拆分 `ProductionOperationConfiguration.cs`，将 532 行多配置单体文件收敛为按 EF configuration 类型命名的独立文件。
- 保持 production pilot 相关 EF table、column、index、conversion、max length、row version 和 column type 不变。
- 不改变 Core 实体、Services 行为、DbContext、repository、store、数据库迁移、部署编排或 MCP 配置。

## 实际改动类别

- 移除原 `ProductionOperationConfiguration.cs` 单体文件。
- 新增 emergency stop、incident、run ledger、pilot window、fixed pilot run、controlled intent、controlled run、GA readiness assessment 的独立 EF configuration 文件。
- 所有新文件保持原 namespace：`AICopilot.EntityFrameworkCore.Configuration.AiGateway`。
- 所有 configuration public 类型名与 `IEntityTypeConfiguration<T>` 实现保持不变。

## 影响模块

- `AICopilot/src/infrastructure/AICopilot.EntityFrameworkCore/Configuration/AiGateway/`
- 影响能力：Infrastructure 层 production pilot EF configuration 文件组织。
- 不影响能力：数据库 schema、迁移、CloudReadonly production pilot 状态机、emergency stop、ledger retention、GA readiness、审批、audit、Core 实体和 Services。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改 Core 实体、Services 行为、DbContext、repository 或 store。
- 未修改数据库迁移、容器编排、MCP 配置。
- 未新增 NuGet/npm/容器依赖。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~MigrationSafetyTests|FullyQualifiedName~EnterpriseCloudReadonlyProductionPilotP12Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionControlledPilotP13Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionOperationsP14Tests|FullyQualifiedName~EnterpriseProductionPilotHardeningP16Tests|FullyQualifiedName~AgentRunQueueProductionOpsTests"`
  - 结果：通过 41/41。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
  - 结果：通过 759/759。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
  - 结果：通过 44/44。
- `git diff --check`
  - 结果：通过，无 whitespace error 输出。
- `git diff --name-only`
  - 结果：当前工作树包含前序多批 AICopilot 拆分改动；本批实际新增/删除文件限定在 production operation EF configuration 拆分文件与本阶段记录。
- `zsh -c 'setopt NULL_GLOB; wc -l src/infrastructure/AICopilot.EntityFrameworkCore/Configuration/AiGateway/*ProductionOperation*.cs src/infrastructure/AICopilot.EntityFrameworkCore/Configuration/AiGateway/*ProductionPilot*.cs src/infrastructure/AICopilot.EntityFrameworkCore/Configuration/AiGateway/*ProductionControlled*.cs'`
  - 结果：所有 production operation EF configuration 文件均低于 500 行。
  - 说明：原单体 `ProductionOperationConfiguration.cs` 已移除，因此在 zsh 下需要启用 `NULL_GLOB` 让已移除文件名模式自然为空。

## 剩余风险

- 本批是纯文件组织拆分，未新增或调整数据库映射语义。
- 未生成 migration，未做真实数据库升级演练；本批不涉及 schema 变化。
- EF 自动 discovery 仍依赖现有 assembly scanning，configuration 类型名和 namespace 未改变。

## 下一阶段进入条件

- 继续从 `AICopilot.BackendTests 759/759` 与 `ArchitectureTests 44/44` 绿色基线进入下一批。
- 下一批可优先评估仍超过 500 行且边界清晰的 AICopilot 后端文件，例如 contracts、DI、Worker 或其他 Core 聚合。
- 如果后续需要改变 table/column/index/conversion/rowversion/schema、DbContext 注册、迁移、实体字段、repository 行为或 production pilot 业务语义，必须单独开批并重新确认范围。
