# AICopilot 后端阶段记录 - SemanticSummaryProfiles 拆分第一批 - 2026-06-01

## 本批目标

- 只修改 `AICopilot` 后端 AiGatewayService 语义摘要链路。
- 拆分 `SemanticSummaryProfiles.cs`，将四类 semantic summary profile 与 formatting helper 从入口 catalog 文件中分离。
- 保持 public catalog/contracts/base 类型契约、摘要文案、metric key、field label、example question、formatting 与 breakdown 行为不变。
- 不修改公开 API/DTO、SQL 语义、数据源映射、数据库迁移、部署编排或 MCP 配置。

## 实际改动类别

- `SemanticSummaryProfiles.cs` 保留 `ISemanticSummaryProfileCatalog`、`ISemanticSummaryProfile`、`SemanticSummaryResponseContract`、`SemanticSummaryProfileCatalog`、`SemanticSummaryProfileBase`。
- 新增 `DeviceSummaryProfile.cs`、`DeviceLogSummaryProfile.cs`、`CapacitySummaryProfile.cs`、`ProductionDataSummaryProfile.cs`，承接四类 target 摘要实现。
- 新增 `SemanticSummaryFormatting.cs`，承接 breakdown、string/decimal/bool/timestamp/number formatting、version parse 与 `VersionKey`。
- 原 `file sealed`/`file static` 类型调整为 `internal sealed`/`internal static`，仅用于同 assembly 跨文件访问，不新增 public API。

## 影响模块

- `AICopilot/src/services/AICopilot.AiGatewayService/BusinessSemantics/`
- 影响能力：语义摘要实现文件组织。
- 不影响能力：BusinessSemanticsCatalog、Intent routing prompt、SemanticAnalysisRunner、Semantic SQL generation、source diagnostics、summary DTO 和前端契约。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 API/DTO、数据库实体、迁移、EF mapping、部署编排或 MCP 配置。
- 未新增 NuGet/npm/容器依赖。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

- `dotnet build src/services/AICopilot.AiGatewayService/AICopilot.AiGatewayService.csproj --no-restore`
  - 结果：通过，0 warning / 0 error。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~BusinessSemanticsCatalogTests|FullyQualifiedName~SemanticAnalysisRunnerTests|FullyQualifiedName~IntentRoutingPromptComposerTests|FullyQualifiedName~SemanticSqlGenerationTests|FullyQualifiedName~SemanticSourceStatusDiagnosticsTests|FullyQualifiedName~ConfiguredSemanticPhysicalMappingProviderTests|FullyQualifiedName~FrontendIntegrationContractTests|FullyQualifiedName~SecurityHardeningTests"`
  - 结果：通过，80 passed / 0 failed。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
  - 结果：通过，759 passed / 0 failed。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
  - 结果：通过，44 passed / 0 failed。
- `git diff --check`
  - 结果：通过，无 whitespace error。
- `git diff --name-only`
  - 结果：通过；输出包含前序 tracked dirty 文件，本批 tracked 修改为 `SemanticSummaryProfiles.cs`，新增文件通过 `git status --short --untracked-files=all` 核对。
- `wc -l src/services/AICopilot.AiGatewayService/BusinessSemantics/*SemanticSummary*.cs src/services/AICopilot.AiGatewayService/BusinessSemantics/*SummaryProfile.cs`
  - 结果：`SemanticSummaryProfiles.cs` 91 行；`SemanticSummaryFormatting.cs` 165 行；`CapacitySummaryProfile.cs` 79 行；`DeviceLogSummaryProfile.cs` 69 行；`DeviceSummaryProfile.cs` 64 行；`ProductionDataSummaryProfile.cs` 75 行。

## 剩余风险

- 本批是语义摘要文件组织拆分，未改变摘要输出行为。
- 当前工作树包含前序多批 AICopilot 拆分改动，本批只追加 SemanticSummaryProfiles 拆分。
- 未做真实部署演练；运行行为由 BackendTests 全量和 ArchitectureTests 覆盖。

## 下一阶段进入条件

- `AICopilot.BackendTests` 与 `AICopilot.ArchitectureTests` 保持绿色。
- 后续可继续评估 `McpServerBootstrap.cs`、`BusinessTextToSql.cs`、`TrialCampaign.cs` 或其他 500 行附近文件。
- 如果后续需要改变摘要输出、field label、metric key、example question、formatting、public contracts、SQL 语义、数据库结构或部署配置，必须单独开批并重新确认范围。
