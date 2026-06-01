# AICopilot 后端阶段记录 - BusinessDatabaseReadonlyQuery 拆分第一批 - 2026-05-28

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `BusinessDatabaseReadonlyQuery.cs`，把只读查询执行、审计记录、数据源治理策略、SQL 安全策略和查询结果映射从单体文件中剥离。
- 保持 `ExecuteBusinessDatabaseReadonlyQueryCommand`、command handler、`BusinessQuerySafetySchema`、`BusinessReadonlyQueryExecutor` public 类型名、构造参数和 `ExecuteAsync` 签名不变。
- 不新增数据源能力，不改变 Text-to-SQL 行为，不改 CloudReadonly 只读边界、数据库迁移、部署编排或 MCP 配置。

## 实际改动

- `BusinessDatabaseReadonlyQuery.cs` 收敛为 contracts/入口文件，仅保留 command、command handler 和 safety schema。
- 新增 `BusinessReadonlyQueryExecutor.cs`，承接只读查询主编排、数据源加载、权限校验、schema 校验、SQL guardrail、查询调用和结果返回。
- 新增 `BusinessReadonlyQueryAuditRecorder.cs`，承接 `queryHash`、`sqlLength`、row/truncation/duration/warning metadata 和 explicit audit save。
- 新增 `BusinessDataSourceGovernancePolicy.cs`，承接 selectable mode、governance status、safety schema 和 readonly credential 校验。
- 新增 `BusinessReadonlyQuerySafetyPolicy.cs`，承接 SELECT-only、table allowlist、sensitive field、system catalog、DDL/DML 和 multi-statement guard。
- 新增 `BusinessQueryResultMapper.cs`，承接 preview row sanitization、query hash、source marker、governance DTO、redacted column/value handling。
- 更新安全源码扫描测试路径：M6 audit metadata 扫描改为读取 audit recorder 与 result mapper；explicit audit save 白名单迁移到 `BusinessReadonlyQueryAuditRecorder.cs`。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.DataAnalysisService/BusinessDatabases`
- 能力边界：BusinessDatabase 只读 SQL 查询、Text-to-SQL 执行复用链路、数据源治理展示辅助策略和查询结果安全映射。
- 公开契约：未修改 command/query/DTO 字段、permission attributes、repository specs、数据库实体、数据库迁移或 DI 注册语义。
- 运行行为：未修改 SQL guardrail、simulation-only 要求、governed schema 要求、query limit、`MaxPreviewRows=50`、sanitized preview、audit payload、warning code 或 explicit audit save 边界。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 DTO/API 语义、数据库迁移、部署编排、MCP 配置或 NuGet/npm/container 依赖。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~AICopilotM5EnterpriseDataSourcePlatformizationTests|FullyQualifiedName~AICopilotM6SecurityComplianceHardeningTests|FullyQualifiedName~EnterpriseDataGovernanceP0Tests|FullyQualifiedName~EnterpriseDataGovernanceP1Tests|FullyQualifiedName~DataSourceAuthorizationTests|FullyQualifiedName~TextToSqlReadOnlyTests|FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~EnterpriseDynamicPlannerP3Tests|FullyQualifiedName~ToolRegistryGovernanceTests"
```

- 结果：通过 165 / 165，失败 0，跳过 0，耗时 653 ms。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 51 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0，耗时 538 ms。

```bash
wc -l src/services/AICopilot.DataAnalysisService/BusinessDatabases/*Business*Readonly*.cs src/services/AICopilot.DataAnalysisService/BusinessDatabases/*Business*Governance*.cs src/services/AICopilot.DataAnalysisService/BusinessDatabases/*Business*QueryResult*.cs
```

- `BusinessDatabaseReadonlyQuery.cs`：38 行。
- `BusinessReadonlyQueryAuditRecorder.cs`：46 行。
- `BusinessReadonlyQueryExecutor.cs`：218 行。
- `BusinessReadonlyQuerySafetyPolicy.cs`：153 行。
- `BusinessDataSourceGovernancePolicy.cs`：124 行。
- `BusinessQueryResultMapper.cs`：200 行。
- 本批拆分文件均低于 500 行。

```bash
git diff --check
```

- 结果：通过，无 whitespace 错误输出。

```bash
git diff --name-only
```

- 结果：输出均为 `AICopilot` 路径，未出现 `IIoT.CloudPlatform/**`、`IIoT.EdgeClient/**` 或 `AICopilot/src/vues/**`。

## 剩余风险

- 本批只拆分 BusinessDatabase readonly query 链路，没有拆 `BusinessDatabaseManagement.cs`、`AiGatewayController.cs`、core 聚合或 CloudRead client。
- `BusinessReadonlyQueryExecutor.cs` 仍保留完整执行主编排；当前 218 行，低于 500，后续如新增执行模式应优先继续拆成更细协作者。
- explicit audit save 仍存在于只读查询审计 recorder 中，语义与原单体一致，仍受 `SecurityHardeningTests` 白名单约束。

## 下一阶段进入条件

- 从当前绿色基线继续：`BackendTests 759/759`、`ArchitectureTests 44/44`。
- 下一批建议单独评估 `BusinessDatabaseManagement.cs`、`AiGatewayController.cs` 或 core domain 中仍超过 500 行的聚合文件。
- 如果下一批涉及 SQL guardrail、audit save 边界、query result DTO、权限策略、数据源选择规则、公开接口、数据库结构或部署配置，必须先出单独计划，不允许改变语义。
