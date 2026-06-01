# AICopilot 后端阶段记录 - CloudReadonlyProductionControlledPilot 拆分第一批 - 2026-05-27

## 本批目标

- 只改 `AICopilot`。
- 将 `CloudReadonlyProductionControlledPilot.cs` 从 P13 controlled production pilot 单体实现收敛为 contracts/status/DTO/command/query/store interface 声明文件。
- 按 P13 controlled production pilot 的 contracts、store、service、handlers、goal policy、query projection 边界拆分实现。
- 保持 P12/P13/P14 gate、ToolRegistry 保护、approval、audit save、intent/plan/run/query result、ledger backfill 语义不变。

## 实际改动类别

- `CloudReadonlyProductionControlledPilot.cs`
  - 保留 P13 statuses、DTO、commands、queries、store interface。
  - 移除 handler、store、service、helper 实现代码。
- `CloudReadonlyProductionControlledPilotHandlers.cs`
  - 承接 status query、create plan command、run command handlers。
  - 保持 `RunCloudReadonlyProductionControlledPilotCommandHandler` 的显式 audit write/save 位置和 payload 字段不变。
- `CloudReadonlyProductionControlledPilotStores.cs`
  - 承接 `InMemoryCloudReadonlyProductionControlledPilotStore`。
  - 承接 `RepositoryCloudReadonlyProductionControlledPilotStore`。
  - 保持 intent 保存、run 保存、DTO/entity 映射、runId 生成和 20 条 run 保留口径不变。
- `CloudReadonlyProductionControlledPilotService.cs`
  - 承接 P13 status 计算、intent 创建、plan intent 校验、intent 执行主编排。
  - 保持 CloudReadonly boundary、ToolRegistry protected tool 校验、emergency stop、CloudAiRead 调用和 P14 ledger backfill 语义不变。
- `CloudReadonlyProductionControlledPilotGoalPolicy.cs`
  - 承接 goal normalize、blocked terms、endpoint allowlist、analysis type、artifact type、time range 归一化。
- `CloudReadonlyProductionControlledPilotQueryProjection.cs`
  - 承接 CloudAiRead query payload 构建、JSON rows 提取和 source metadata projection。
- `SecurityHardeningTests.cs`
  - 将 explicit audit save 白名单从旧单体文件更新为 `CloudReadonlyProductionControlledPilotHandlers.cs`。

## 影响模块

- `AICopilot.AiGatewayService/CloudReadiness`
  - P13 controlled production pilot 内部结构。
- `AICopilot.BackendTests`
  - 安全扫描测试中的 P13 explicit audit save 文件路径。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 contracts/DTO 字段语义、数据库迁移、部署编排、MCP 配置或容器配置。
- 未修改 P12/P13/P14/P16 业务状态字符串、审批 gate、ToolRegistry 保护策略、CloudReadonly 只读防护或 audit payload 语义。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~EnterpriseCloudReadonlyProductionPilotP12Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionControlledPilotP13Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionOperationsP14Tests|FullyQualifiedName~EnterpriseProductionPilotHardeningP16Tests|FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~ToolRegistryGovernanceTests"`
  - 结果：通过 133/133。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
  - 结果：通过 759/759。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
  - 结果：通过 44/44。
- `git diff --check`
  - 结果：通过，无 whitespace/error 输出。
- `wc -l src/services/AICopilot.AiGatewayService/CloudReadiness/*ProductionControlledPilot*.cs`
  - `CloudReadonlyProductionControlledPilot.cs`：114 行。
  - `CloudReadonlyProductionControlledPilotGoalPolicy.cs`：192 行。
  - `CloudReadonlyProductionControlledPilotHandlers.cs`：133 行。
  - `CloudReadonlyProductionControlledPilotQueryProjection.cs`：128 行。
  - `CloudReadonlyProductionControlledPilotService.cs`：460 行。
  - `CloudReadonlyProductionControlledPilotStores.cs`：245 行。

## 剩余风险

- `CloudReadonlyPilotReadiness.cs`、`CloudReadonlyReadiness.cs`、`CloudReadonlySandboxAgentTrial.cs`、`CloudReadonlySandboxControlledTrial.cs` 仍超过或接近 500 行，可作为后续 CloudReadiness 结构债批次。
- MCP 独立容器部署仍未处理，保持为后续部署批次。
- 本批是结构拆分，不改变 CloudAiRead 真实接口可用性；真实环境仍依赖 CloudAiRead 配置、Cloud 只读接口与部署网络状态。

## 下一阶段进入条件

- 继续从当前绿色基线进入下一批。
- 优先候选：`CloudReadonlySandboxControlledTrial.cs` 或 `CloudReadonlyReadiness.cs` 拆分第一批。
- 进入前继续执行 `docs/AI执行门禁精华.md`、`docs/计划执行规则.md`、`AICopilot/AGENTS.md` 和 `docs/跨项目对齐规则.md` 门禁。
