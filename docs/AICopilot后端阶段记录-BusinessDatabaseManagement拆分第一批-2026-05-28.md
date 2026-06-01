# AICopilot 后端阶段记录 - BusinessDatabaseManagement 拆分第一批 - 2026-05-28

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `BusinessDatabaseManagement.cs`，把业务库配置命令、数据源授权命令、查询 handlers、DTO mapper 和安全校验器从单体文件中剥离。
- 保持 DTO、command/query records、public handler 类型名、构造参数、`Handle` 签名、权限标注和 DI 注册语义不变。
- 不新增数据源管理能力，不改 Text-to-SQL 行为，不改 CloudReadonly 只读边界、数据库迁移、部署编排或 MCP 配置。

## 实际改动

- `BusinessDatabaseManagement.cs` 收敛为 contracts/入口文件，仅保留 DTO、command records 和 query records。
- 新增 `BusinessDatabaseCommandHandlers.cs`，承接 create/update/delete handlers，并保留 audit write 与 repository save 边界。
- 新增 `DataSourcePermissionCommandHandlers.cs`，承接 grant/revoke permission handlers，保留 grant target normalize、disable revoke 和 audit 语义。
- 新增 `BusinessDatabaseQueryHandlers.cs`，承接 get/list/my-authorized query handlers，保留 metadata/query 授权过滤和 selection mode 过滤。
- 新增 `BusinessDatabaseDtoMapper.cs`，承接 `BusinessDatabaseDtoMapper` 与 `DataSourcePermissionGrantDtoMapper`。
- 新增 `BusinessDatabaseSafetyValidator.cs`，承接 provider/read-only/credential/query-limit 校验。
- 更新 `SecurityHardeningTests` 源码扫描路径：config command audit 扫描改到 command handlers；DataAnalysis audit summary 扫描改为组合 command handlers 与 DTO mapper。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.DataAnalysisService/BusinessDatabases`
- 能力边界：BusinessDatabase 配置管理、DataSource permission grant/revoke、业务库 metadata/query authorized list。
- 公开契约：未修改 DTO 字段、command/query records、permission attributes、repository specs、数据库实体或数据库迁移。
- 运行行为：未修改 create/update/delete、permission grant/revoke、metadata access、query access、audit payload、connection string mask、governance status 或 readonly credential 校验语义。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 DTO/API 语义、数据库迁移、部署编排、MCP 配置或 NuGet/npm/container 依赖。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~AICopilotM5EnterpriseDataSourcePlatformizationTests|FullyQualifiedName~EnterpriseDataGovernanceP0Tests|FullyQualifiedName~EnterpriseDataGovernanceP1Tests|FullyQualifiedName~DataSourceAuthorizationTests|FullyQualifiedName~TextToSqlReadOnlyTests|FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~FreshDatabaseSeedTests|FullyQualifiedName~IdentityAccessManagementTests"
```

- 结果：通过 116 / 116，失败 0，跳过 0，耗时 1 m 2 s。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 37 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0，耗时 545 ms。

```bash
wc -l src/services/AICopilot.DataAnalysisService/BusinessDatabases/*BusinessDatabase*.cs src/services/AICopilot.DataAnalysisService/BusinessDatabases/*DataSourcePermission*.cs
```

- `BusinessDatabaseManagement.cs`：115 行。
- `BusinessDatabaseCommandHandlers.cs`：290 行。
- `BusinessDatabaseQueryHandlers.cs`：68 行。
- `BusinessDatabaseDtoMapper.cs`：64 行。
- `BusinessDatabaseSafetyValidator.cs`：59 行。
- `DataSourcePermissionCommandHandlers.cs`：138 行。
- 通配符也包含既有 `BusinessDatabaseAccessService.cs`、`BusinessDatabaseContractMapper.cs`、`BusinessDatabaseReadService.cs`、`BusinessDatabaseReadonlyQuery.cs`，相关文件均低于 500 行。

```bash
git diff --check
```

- 结果：通过，无 whitespace 错误输出。

```bash
git diff --name-only
```

- 结果：输出均为 `AICopilot` 路径，未出现 `IIoT.CloudPlatform/**`、`IIoT.EdgeClient/**` 或 `AICopilot/src/vues/**`。

## 剩余风险

- 本批只处理 BusinessDatabaseManagement 单体，没有拆 `AiGatewayController.cs`、core 聚合、CloudRead client 或测试大文件。
- `BusinessDatabaseCommandHandlers.cs` 仍集中承载 create/update/delete 三个命令 handler；当前 290 行，低于 500，后续新增命令时应优先按 command family 继续拆。
- 本批没有新增 explicit audit save；config command audit 仍随 repository save 进入既有事务边界。

## 下一阶段进入条件

- 从当前绿色基线继续：`BackendTests 759/759`、`ArchitectureTests 44/44`。
- 下一批建议单独评估 `CloudAiReadClient.cs`、`AiGatewayController.cs` 或 core domain 中仍超过 500 行的聚合文件。
- 如果下一批涉及数据源 API、权限策略、审计 save 边界、readonly credential 校验、DTO 映射、公开接口、数据库结构或部署配置，必须先出单独计划，不允许改变语义。
