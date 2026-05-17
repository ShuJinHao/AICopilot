# AICopilot 后端阶段记录：Cloud 只读 Agent 闭环

日期：2026-05-15

## 改动范围

- 仅修改 AICopilot 后端、后端测试和后端记录文档。
- 未修改 `src/vues` 前端文件；当前 `src/vues` dirty 内容仍视为外部既有变更。
- 未修改 `IIoT.CloudPlatform`、`IIoT.EdgeClient`，未新增 Cloud 项目引用，未直接访问 Cloud 业务库。

## 本批完成

- `CloudDataReport` 计划阶段接入 Cloud 只读意图收敛，`AgentTaskDto.planJson` 新增 `cloudReadonlyIntent`。
- 新增 Cloud 只读 Agent 执行器，Runtime 通过 `ICloudAiReadClient.QuerySemanticAsync` 执行设备、设备日志、产能、过站/生产记录只读查询。
- Recipe、Policy、General、低置信、写语义、SQL/连接串/密钥类 payload 会被拒绝为 `cloud_readonly_intent_unsupported`。
- Runtime 保持 Tool Registry 二次校验；`query_cloud_data_readonly` 仍默认 disabled，管理员启用后才可进入计划。
- Cloud 查询成功后，摘要、来源、截断状态、行数、规范化 rows 会进入运行态，供 Markdown/HTML/Chart 等产物使用。
- Cloud 查询失败时停止任务，不继续生成伪报告；`ToolExecutionRecord` 使用 CloudAiRead 稳定错误码并继续脱敏。
- `MigrationWorkApp` seed 不再覆盖管理员已调整的工具 `isEnabled`、`requiresApproval`、`riskLevel` 等治理字段。
- `CloudAiReadClient` 支持仅有 `deviceCode` 时作为 Cloud 设备参数传递，仍保留缺少设备条件和时间范围时 fail-fast。

## 接口变化

- 不新增前端路由，不改变 `/api/aigateway/*` 路径。
- `AgentTaskDto.planJson` 新增可选字段 `cloudReadonlyIntent`：
  - `intent`
  - `query`
  - `confidence`
  - `target`
  - `kind`
  - `summary`
- 新增错误码：`cloud_readonly_intent_unsupported`。

## 验证结果

- `dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj`：通过，0 warning，0 error。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName~ToolRegistryGovernanceTests"`：通过，10 个测试。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName~CloudAiReadClientTests|FullyQualifiedName~SemanticQueryPlannerTests|FullyQualifiedName~SemanticAnalysisRunnerTests"`：通过，52 个测试。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName!~FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload"`：通过，446 个测试。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj`：通过，44 个测试。
- `dotnet test src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj`：通过，6 个测试。
- `dotnet list src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj package --vulnerable --include-transitive`：未发现易受攻击的包。

## 剩余风险

- `query_cloud_data_readonly` 默认仍 disabled；需要管理员通过 Tool Registry 显式启用后才可走真实 Cloud AiRead。
- Cloud AiRead 端点的真实可用性依赖运行配置 `CloudAiRead` 和 Cloud 侧只读接口部署状态。
- 完整 BackendTests 仍按计划排除前端源码断言 `FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload`，原因是当前 `src/vues` dirty 内容不属于本批后端改造范围。
