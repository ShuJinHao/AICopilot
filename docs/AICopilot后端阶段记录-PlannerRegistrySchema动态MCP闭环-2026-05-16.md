# AICopilot 后端阶段记录：Planner Registry Schema 动态 MCP 闭环

日期：2026-05-16

## 改动范围

- 仅修改 `AICopilot` 后端、后端测试和阶段记录。
- 未修改 `src/vues`，未修改 `IIoT.CloudPlatform`，未修改 `IIoT.EdgeClient`。
- 未新增 NuGet 包，未引入完整 JSON Schema 引擎。

## 完成内容

- 新增内部 `PlannerToolCatalog`，由 Tool Registry、当前用户权限、MCP runtime availability 生成动态 Planner 可用工具目录。
- Planner catalog 只包含 registered、enabled、not blocked、当前用户有权限、MCP runtime available 的工具。
- Catalog 暴露工具的 provider/target、risk、approval、timeout、audit、input/output schema 摘要，并统一脱敏 API Key、token、连接串、服务器绝对路径、SQL/表名。
- `DefaultAgentDynamicPlanner` 输入改为使用 catalog/schema 摘要，不再把 raw schema 直接作为 Planner 输入。
- 动态 Planner 空可用工具目录返回稳定错误码 `planner_tool_catalog_empty`。
- 不支持或非法的 registry schema 返回稳定错误码 `planner_tool_schema_unsupported`。
- `AgentTaskDto.planJson` 兼容新增：
  - `plannerToolCatalogVersion`
  - `plannerAvailableToolCount`
- MCP 工具仍必须 enabled、runtime available、schema 校验通过，并继续走 approval、runtime guard、`McpAgentToolExecutor`、`ToolExecutionRecord`。

## 接口与契约变化

- `/api/aigateway/agent/task/plan` 路由不变。
- `planJson` 保留既有 `plannerMode`、`plannerModelId`、`plannerValidationVersion`、`steps[].inputJson`。
- 新增兼容字段：
  - `plannerToolCatalogVersion`
  - `plannerAvailableToolCount`
- 新增错误码：
  - `planner_tool_catalog_empty`
  - `planner_tool_schema_unsupported`

## 验证结果

- `dotnet build .\src\hosts\AICopilot.HttpApi\AICopilot.HttpApi.csproj --no-restore`
  - 通过，0 warnings，0 errors。
- `dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj --filter "Suite=ToolRegistryGovernance|Suite=DynamicPlannerContract"`
  - 通过，34 tests。
- `dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj --filter "FullyQualifiedName!~FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload" --no-build`
  - 通过，472 tests，耗时约 4 分 34 秒。
- `dotnet test .\src\tests\AICopilot.ArchitectureTests\AICopilot.ArchitectureTests.csproj --no-restore`
  - 通过，44 tests。
- `dotnet test .\src\tests\AICopilot.AiEvalTests\AICopilot.AiEvalTests.csproj --no-restore`
  - 通过，6 tests。
- `dotnet list .\src\hosts\AICopilot.HttpApi\AICopilot.HttpApi.csproj package --vulnerable --include-transitive`
  - 通过，未发现易受攻击包。

## 剩余风险

- 动态 Planner 仍是受控 MVP，只能从后端 catalog 选择工具，不开放自由工具发现。
- JSON Schema 校验仍是 `System.Text.Json` 子集实现，复杂 schema 需要后续单独扩展。
- CloudReadonly 工具仍默认 disabled，配方主数据和配方版本继续禁读。
- 当前 `src/vues` dirty 内容仍为外部阻塞项，本批未处理。
