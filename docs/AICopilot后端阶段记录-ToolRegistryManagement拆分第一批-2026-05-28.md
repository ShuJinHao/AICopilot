# AICopilot 后端阶段记录 - ToolRegistryManagement 拆分第一批 - 2026-05-28

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `ToolRegistryManagement.cs`，把 query handlers、command handlers、update policy、mapper 和 execution record sanitizer 从单体文件中剥离。
- 保持 ToolRegistry DTO、queries、commands、public handler 类型名、构造参数、`Handle` 签名、权限标注和 DI 注册语义不变。
- 不新增工具治理功能，不改 ToolRegistry data boundary，不改 planner/agent 执行权限，不改 CloudReadonly 只读防护、数据库迁移、部署编排或 MCP 配置。

## 实际改动

- `ToolRegistryManagement.cs` 收敛为 contracts/入口文件，仅保留 tool registry DTO、query records 和 command records。
- 新增 `ToolRegistryQueryHandlers.cs`，承接 list/detail/catalog query handlers。
- 新增 `ToolRegistryCommandHandlers.cs`，承接 update/upsert/activate/disable command handlers，并保留原有 audit write 与 repository save 边界。
- 新增 `ToolRegistryUpdatePolicy.cs`，承接 changed fields、data boundary parse 和 update 辅助逻辑。
- 新增 `ToolRegistrationMapper.cs`，承接 `ToolRegistrationMapper` 的 tool registration 与 tool execution record DTO 映射。
- 新增 `ToolExecutionRecordSanitizer.cs`，承接敏感信息裁剪与脱敏 regex。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.AiGatewayService/Tools`
- 能力边界：ToolRegistry 查询、工具定义管理、catalog projection、tool execution record DTO 映射和安全脱敏内部结构拆分。
- 公开契约：未修改 DTO 字段、query、command、permission attributes、status/risk/data-boundary 字符串、repository specs 或数据库实体。
- 运行行为：未修改 tool update/upsert/activate/disable、runtime availability、MCP source server projection、audit payload、sanitizer redaction 或 protected CloudReadonly tool force-disable 行为。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 DTO/API 语义、数据库迁移、部署编排、MCP 配置或 NuGet/npm/container 依赖。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~ToolRegistryGovernanceTests|FullyQualifiedName~McpToolGovernanceTests|FullyQualifiedName~McpRuntimeRegistrySynchronizerTests|FullyQualifiedName~FreshDatabaseSeedTests|FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~EnterpriseDynamicPlannerP3Tests|FullyQualifiedName~EnterpriseCloudReadonlyPilotReadinessP11Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionPilotP12Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionControlledPilotP13Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionOperationsP14Tests"
```

- 结果：通过 151 / 151，失败 0，跳过 0，耗时 43 s。
- 备注：首次运行发现 `ToolRegistryManagement.cs` 缺少 `AICopilot.SharedKernel.Ai` using，补齐后通过。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 40 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0，耗时 547 ms。

```bash
wc -l src/services/AICopilot.AiGatewayService/Tools/*ToolRegistry*.cs src/services/AICopilot.AiGatewayService/Tools/*ToolRegistration*.cs src/services/AICopilot.AiGatewayService/Tools/*ToolExecutionRecord*.cs
```

- `ToolRegistryManagement.cs`：156 行。
- `ToolRegistryQueryHandlers.cs`：86 行。
- `ToolRegistryCommandHandlers.cs`：326 行。
- `ToolRegistryUpdatePolicy.cs`：104 行。
- `ToolRegistrationMapper.cs`：103 行。
- `ToolExecutionRecordSanitizer.cs`：37 行。
- 备注：通配符也匹配既有 `McpToolRegistryReadService.cs` 与 `ToolRegistryGuard.cs`，本批新增/拆分文件均低于 500 行。

```bash
git diff --check
```

- 结果：通过，无 whitespace 错误输出。

```bash
git diff --name-only
```

- 结果：输出均为 `AICopilot` 路径，未出现 `IIoT.CloudPlatform/**`、`IIoT.EdgeClient/**` 或 `AICopilot/src/vues/**`。
- 说明：该命令不列未跟踪新增文件；本批新增 split 文件和阶段记录见“实际改动”。

## 剩余风险

- 本批只处理 ToolRegistryManagement 单体，没有拆 `AiGatewayController.cs`、core domain aggregate、DataAnalysis 查询/管理单体或 CloudRead client。
- `ToolRegistryCommandHandlers.cs` 仍集中承载四个命令 handler；当前 326 行，低于 500，后续若新增命令应优先再按 command family 拆分。
- 本批没有修改 security whitelist，因为 command handlers 使用的是 `auditLogWriter.WriteAsync` 加 repository save，没有新增 `auditLogWriter.SaveChangesAsync`。

## 下一阶段进入条件

- 从当前绿色基线继续：`BackendTests 759/759`、`ArchitectureTests 44/44`。
- 下一批建议单独评估 `BusinessDatabaseReadonlyQuery.cs`、`BusinessDatabaseManagement.cs` 或 `CloudAiReadClient.cs`，继续避开 controller 路由和 core domain 聚合，除非先出专门计划。
- 如果下一批涉及 ToolRegistry API、权限策略、ToolRegistry guard、protected CloudReadonly safe-state、audit save 边界、公开接口、数据库结构或部署配置，必须先出单独计划，不允许改变语义。
