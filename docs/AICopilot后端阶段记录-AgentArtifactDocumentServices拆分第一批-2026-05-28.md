# AICopilot 后端阶段记录 - AgentArtifactDocumentServices 拆分第一批 - 2026-05-28

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `AgentArtifactDocumentServices.cs`，把表格文件解析、PDF、PPTX、XLSX、共享格式化和 PDF 字体解析从单体文件中剥离。
- 保持 `AgentTableFileParser`、`AgentArtifactDocumentGenerator` 的 public 入口、构造语义、DI 注册语义和测试直接构造路径不变。
- 不新增 artifact 类型，不改 report/document 输出格式，不改前端、数据库迁移、部署编排或 MCP 配置。

## 实际改动

- `AgentArtifactDocumentServices.cs` 收敛为薄门面，仅保留 `AgentTableFileParser` 和 `AgentArtifactDocumentGenerator` 两个既有 public 入口。
- 新增 `AgentTableFileParserCore.cs`，承接 CSV/JSON/XLSX 表格解析、JSON row projection、CSV 行解析、XLSX cell 读取和列名归一化。
- 新增 `AgentPdfDocumentGenerator.cs`，承接 PDF 文档生成、文本行绘制、Data Source、Metrics、Inputs、Tables 和 Knowledge Sources 渲染。
- 新增 `AgentPptxDocumentGenerator.cs`，承接 PPTX slide、text shape 和 slide body 构建。
- 新增 `AgentXlsxDocumentGenerator.cs`，承接 workbook、worksheet、sheet name、inline string cell 和 summary/data/sources sheet 写入。
- 新增 `AgentArtifactDocumentFormatting.cs`，承接 source marker、bool formatting、summary/data/sources table 构建。
- 新增 `AgentPdfFontResolver.cs`，承接 PDF font resolver，并保留上一批已加入的 macOS 字体候选路径。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/infrastructure/AICopilot.Infrastructure/Artifacts`
- 能力边界：artifact document generation 和 uploaded table file parsing 内部结构拆分。
- 公开契约：未修改 `IAgentTableFileParser`、`IAgentArtifactDocumentGenerator`、`AgentReportDocument`、`AgentReportTable`、`AgentReportMetric` 或 source metadata contracts。
- 运行行为：未修改 PDF/PPTX/XLSX 输出语义、sheet/table 名称、source marker、queryHash/resultHash、row projection、表格解析容错或异常消息。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 DTO/API 语义、数据库迁移、部署编排、MCP 配置或 NuGet/npm/container 依赖。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~AgentArtifactGenerationTests|FullyQualifiedName~EnterpriseAgentWorkbenchP2Tests|FullyQualifiedName~AgentArtifactVersioningTests|FullyQualifiedName~EnterpriseArtifactWorkspaceP9Tests|FullyQualifiedName~FrontendIntegrationContractTests|FullyQualifiedName~AcceptanceClosureVerificationTests|FullyQualifiedName~ToolRegistryGovernanceTests"
```

- 结果：通过 66 / 66，失败 0，跳过 0，耗时 56 s。
- 备注：首次运行发现 `AgentXlsxDocumentGenerator.cs` 缺少 `DocumentFormat.OpenXml` using，补齐后通过。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 40 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0，耗时 539 ms。

```bash
wc -l src/infrastructure/AICopilot.Infrastructure/Artifacts/*AgentArtifactDocument*.cs src/infrastructure/AICopilot.Infrastructure/Artifacts/*AgentTable*.cs src/infrastructure/AICopilot.Infrastructure/Artifacts/*DocumentFormatting*.cs src/infrastructure/AICopilot.Infrastructure/Artifacts/*Pdf*.cs src/infrastructure/AICopilot.Infrastructure/Artifacts/*Pptx*.cs src/infrastructure/AICopilot.Infrastructure/Artifacts/*Xlsx*.cs
```

- `AgentArtifactDocumentServices.cs`：37 行。
- `AgentTableFileParserCore.cs`：295 行。
- `AgentArtifactDocumentFormatting.cs`：211 行。
- `AgentPdfDocumentGenerator.cs`：81 行。
- `AgentPdfFontResolver.cs`：60 行。
- `AgentPptxDocumentGenerator.cs`：115 行。
- `AgentXlsxDocumentGenerator.cs`：80 行。
- 备注：命令通配符会重复匹配 `AgentArtifactDocumentFormatting.cs`，但单文件行数均低于 500。

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

- 本批只处理 artifact document infrastructure，没有处理 `AiGatewayController.cs`、core domain aggregate、ToolRegistry 或 DataAnalysis 侧更大的结构债。
- PDF/PPTX/XLSX 生成仍是静态 helper 组织，未引入 DI 或可替换策略；这是为了保持本批为低风险搬移式拆分。
- 后续如果新增 artifact 输出格式，应优先落到独立 generator/helper，避免重新堆回 `AgentArtifactDocumentServices.cs`。

## 下一阶段进入条件

- 从当前绿色基线继续：`BackendTests 759/759`、`ArchitectureTests 44/44`。
- 下一批建议单独评估 `ToolRegistryManagement.cs`、`BusinessDatabaseReadonlyQuery.cs`、`BusinessDatabaseManagement.cs` 或 `CloudAiReadClient.cs`，不要顺手扩到 controller、core domain、部署或 UI。
- 如果下一批涉及 artifact 输出契约、文件存储契约、ToolRegistry data boundary、CloudReadonly 只读边界、公开接口、数据库结构或部署配置，必须先出单独计划，不允许改变语义。
