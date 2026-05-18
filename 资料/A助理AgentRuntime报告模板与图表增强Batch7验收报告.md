# A助理 Agent Runtime 报告模板与图表增强 Batch 7 验收报告

- 批次：Batch 7 报告模板与图表增强
- 范围：AICopilot 后端、后端测试、验收文档
- Cloud/Edge 触碰：否
- 前端 `src/vues` 触碰：否
- 真实 Cloud 访问引入：否
- shell 工具能力引入：否
- 任意服务器路径写入引入：否
- 新增 NuGet/npm 依赖：否
- 数据库迁移：否

## 实际改动

- 新增统一报告组合器 `AgentReportComposer`，Markdown、HTML、PDF、PPTX、XLSX 均从 `AgentReportDocument` 派生。
- 扩展报告契约，新增指标摘要 `AgentReportMetric` 与来源结构 `AgentReportSourceInfo`。
- CloudReadonly Simulation 数据进入报告模型时统一写入：
  - `sourceMode=Simulation`
  - `isSimulation=true`
  - `sourceLabel=模拟 Cloud 只读数据`
- `charts/chart-data.json` 升级为 chart v2：
  - `schemaVersion`
  - `chartType`
  - `labels`
  - `series`
  - `sourceInfo`
  - `rowCount`
  - `truncated`
  - `generatedAt`
  - 保留兼容字段 `type/source/labels/values`
- PDF/PPTX 增加目标、来源、指标摘要、表格摘要和知识来源摘要。
- XLSX 固定输出 `Summary`、`Data`、`Sources` 三个工作表。
- Runtime Simulation acceptance 改为解析 chart JSON 断言来源结构，并断言 Markdown/HTML 均带 Simulation 标记。

## 验收命令

- `dotnet build src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug`
- `dotnet test src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=AgentArtifact|Suite=Batch7ReportArtifacts"`
- `dotnet test src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --filter "FullyQualifiedName~ToolRegistryGovernanceTests.AgentTaskRuntime_ShouldExecuteCloudReadonlyTool_AndUseRowsInMarkdownReport|Suite=AgentSimulationAcceptance"`
- `dotnet test src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=Batch5ApprovalHardening|Suite=Batch6SecretProtection|FullyQualifiedName~SecretStringEncryptorTests|FullyQualifiedName~FreshDatabaseSeedTests|FullyQualifiedName~IdentityAccessManagementTests"`
- `.\scripts\Run-AgentSimulationAcceptance.ps1`

## 测试结果

- 后端测试项目构建：通过，0 warning，0 error。
- Batch 7 报告与图表测试：通过，16 passed。
- Simulation runtime/CloudReadonly 回归：通过，5 passed。
- Batch 5 审批权限 + Batch 6 密钥保护 + seed/权限目录回归：通过，20 passed。
- Batch 0-4 离线 Simulation 验收脚本：通过。

## 边界确认

- 未修改 `IIoT.CloudPlatform`。
- 未修改 `IIoT.EdgeClient`。
- 未修改或格式化 `src/vues` 既有 dirty 文件。
- 未开启真实 Cloud 读取。
- 未改变 `CloudReadonly.Mode=Disabled` 默认值。
- 未削弱审批、final review、finalize、下载或审计流程。
- 未把 Simulation 数据伪装为真实数据。

## 剩余事项

- 本批不处理产物版本链、差异对比、回滚和修改闭环；这些保持留到 Batch 8。
- PDF/PPTX 仅做结构内容增强，未做视觉精修。
