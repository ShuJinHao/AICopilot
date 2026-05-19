# A助理 Agent Runtime 离线 Simulation 功能闭环交付索引

本索引用于串联 Batch 0-11 的交付材料。当前交付口径是 AICopilot 后端离线 Simulation 闭环，不包含真实 Cloud 接入、真实前端联调或 Cloud/Edge 修改。

## 批次索引

| 批次 | 交付内容 | 验收记录 |
| --- | --- | --- |
| Batch 0-4 | 离线 Simulation 范围冻结、CloudReadonly 三态、制造业 Simulation 数据集、Simulation provider、后端离线验收脚本 | `资料/A助理AgentSimulation离线验收报告.md` |
| Batch 5 | 审批权限硬化，普通 User 不能审批 ToolCall / FinalOutput，不能 finalize | `资料/A助理AgentRuntime审批权限硬化Batch5验收报告.md` |
| Batch 6 | `LanguageModel.ApiKey` / `EmbeddingModel.ApiKey` 新写入加密入库，运行时按需解密，旧明文拒绝读取 | `资料/A助理AgentRuntime密钥入库保护Batch6验收报告.md` |
| Batch 7 | 统一报告模型、来源标记、chart v2 payload、Markdown/HTML/PDF/PPTX/XLSX 内容一致性 | `资料/A助理AgentRuntime报告模板与图表增强Batch7验收报告.md` |
| Batch 8 | 文本类 draft artifact 内容修改、版本历史、下载、diff、restore | `资料/A助理AgentRuntime产物修改与版本闭环Batch8验收报告.md` |
| Batch 9 | MCP 工具治理只读视图，展示 allowlist、注册、runtime 可用性和漂移状态 | `资料/A助理AgentRuntimeMCP工具发现与治理Batch9验收报告.md` |
| Batch 10 | Agent Run Queue / DataWorker 运维指标、retry backoff、stale lease fail、幂等 cancel、运维审计 | `资料/A助理AgentRuntime队列生产运维增强Batch10验收报告.md` |
| Batch 11 | 文档、验收和交付冻结，汇总 Batch 0-10 能力边界和后续进入条件 | `资料/A助理AgentRuntime离线Simulation功能闭环Batch11交付冻结报告.md` |

## 当前冻结边界

- `CloudReadonly` 默认仍为 `Disabled`。
- Simulation 输出必须保留 `sourceMode=Simulation`、`isSimulation=true`、`sourceLabel=模拟 Cloud 只读数据`。
- 不接真实 Cloud，不写真实 Cloud 业务数据，不让模型拼 SQL，不开放 shell，不允许任意服务器路径写入。
- 不改 `IIoT.CloudPlatform`，不改 `IIoT.EdgeClient`，不改前端 `src/vues` dirty 文件。
- 不新增数据库迁移，不新增 NuGet/npm 依赖。

## 最新验收命令

- `dotnet build .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug`
- `dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=Batch5ApprovalHardening|Suite=Batch6SecretProtection|Suite=Batch7ReportArtifacts|Suite=Batch8ArtifactVersioning"`
- `dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=Batch9McpToolGovernance|Suite=Batch10RunQueueOps"`
- `dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=ToolRegistryGovernance"`
- `dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=AgentSimulationAcceptance"`
- `.\scripts\Run-AgentSimulationAcceptance.ps1`
- `.\scripts\Test-AgentSimulationScope.ps1 -ChangedFiles <Batch11文档清单>`

## 后续不得混入本批的事项

- 真实 Cloud 只读接入。
- 真实前端联调或 `src/vues` dirty 文件整理。
- Cloud/Edge 代码修改。
- 数据库迁移、依赖升级、部署脚本改造。
- 外部 MCP server 主动发现或工具执行。
- 自动恢复 stale lease 或外部告警推送。
