# AICopilot 后端阶段记录：Tool Executor + MCP 受控执行 MVP

日期：2026-05-15

## 改动范围

- 仅修改 `AICopilot` 后端、后端测试和后端阶段文档。
- 未修改 `src/vues`，当前前端 dirty 内容继续作为外部阻塞项记录。
- 未修改 `IIoT.CloudPlatform`、`IIoT.EdgeClient`，未新增 Cloud 项目引用。

## 本批改动

- 新增 `IAgentToolExecutor`、`AgentToolExecutionContext`、`AgentToolExecutionResult`、`AgentToolExecutorResolver`。
- `AgentTaskRuntime` 执行 step 时统一走 Tool Registry gate、executor resolver、timeout、审计元数据和 `ToolExecutionRecord`。
- 现有内置工具、CloudReadonly、artifact 工具保持原行为，通过 runtime 内置 executor 适配到统一执行链。
- 新增 `McpAgentToolExecutor`：只执行 registry 中已启用、权限通过、审批通过、runtime 可解析的 MCP 工具；输入做 registry schema 基础校验，输出做截断和脱敏。
- MCP bootstrap/discovery 后将可暴露 MCP tool upsert 到 Tool Registry；新发现工具默认 `isEnabled=false`、`requiresApproval=true`、`providerType=Mcp`、`targetType=McpServer`，再次发现不覆盖管理员已调整的 enabled/approval/risk/permission/timeout/audit 设置。
- `ToolRegistrationDto` 增加只读说明字段：`runtimeAvailable`、`lastDiscoveredAt`、`sourceServerName`。
- 新增错误码：`tool_input_invalid`、`tool_execution_timeout`。

## 接口变化

- `/api/aigateway/tools`、`/api/aigateway/tools/{toolCode}`、`PATCH /api/aigateway/tools/{toolCode}` 路由不变。
- Tool Registry DTO 增加 MCP runtime 可见性字段，前端不识别时不影响现有字段消费。
- `/api/aigateway/agent/task/{id}/tool-executions` 路由不变，MCP 成功、失败、拒绝、超时沿用统一执行记录。

## 验证命令

- `dotnet build AICopilot/src/services/AICopilot.AiGatewayService/AICopilot.AiGatewayService.csproj`
- `dotnet build AICopilot/src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj`
- `dotnet test AICopilot/src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Suite=ToolRegistryGovernance"`
- `dotnet test AICopilot/src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-build --filter "FullyQualifiedName!~FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload"`
- `dotnet test AICopilot/src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj`
- `dotnet test AICopilot/src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj`
- `dotnet list AICopilot/src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj package --vulnerable --include-transitive`

## 当前结果

- Service build：通过。
- HttpApi build：通过。
- ToolRegistryGovernance：17 项通过。
- BackendTests：454 项通过（按既定方式排除前端源码断言；先前带构建全量命令在 5 分钟超时，单独 build 后 `--no-build` 回归通过）。
- ArchitectureTests：44 项通过。
- AiEvalTests：6 项通过。
- 漏洞检查：未发现易受攻击包。

## 剩余风险

- 本批仍保留内置工具执行 switch，但入口已收敛到 executor；后续可继续把每类 built-in 工具拆成独立 executor。
- MCP 输入 schema 目前做 required/type 基础校验，未实现完整 JSON Schema validator。
- 动态 Planner 仍未开放读取 registry schema 自动生成任意 MCP plan，继续留到下一批。
- CloudReadonly 工具仍默认 disabled，配方主数据和配方版本继续禁读。
