# AICopilot 后端阶段记录：动态 Planner MVP + Plan Schema 治理

日期：2026-05-16

## 改动范围

- 仅修改 `AICopilot` 后端、后端测试和后端阶段记录。
- 未修改 `src/vues`；当前前端 dirty 内容继续作为外部阻塞项记录。
- 未修改 `IIoT.CloudPlatform`、`IIoT.EdgeClient`，未新增 Cloud 项目引用，未直接访问 Cloud 业务库。

## 本批改动

- `LanguageModelUsage` 增加 `Planner` flag；语言模型 DTO、测试模型契约支持返回和接收 `Planner` usage，API Key 仍只返回 `hasApiKey` 与固定 `apiKeyPreview=******`。
- `PlanAgentTaskCommandHandler` 增加 Planner 模型解析：
  - 显式指定不可用或不支持 `Planner` 的模型时返回 `planner_model_unavailable`。
  - 未指定且无可用 Planner 模型时继续走现有静态 plan。
  - 有可用 Planner 模型时调用动态 Planner 生成受控 JSON plan。
- 新增 `IAgentDynamicPlanner` / `DefaultAgentDynamicPlanner`，模型输入只包含 goal、taskType、uploadIds 摘要、knowledgeBaseIds 摘要和当前用户可用 Tool Registry 工具摘要。
- `AgentTaskPlanDocument.steps[]` 增加兼容字段 `inputJson`；`AgentStep` 创建链路同步保存 `InputJson`。
- 新增 `AgentPlanToolGuard`，Planner 阶段统一校验工具 registered、enabled、not blocked、权限、CloudReadonly 边界、MCP runtime availability，并合并 registry approval 要求。
- 新增 `ToolInputSchemaValidator`，基于 `System.Text.Json` 实现确定性 JSON Schema 子集校验：`type`、`properties`、`required`、`enum`、基础数组和对象结构。
- `AgentTaskRuntime` 执行前按 Tool Registry `InputSchemaJson` 二次校验 `step.InputJson`；即使 planJson 被篡改也会拒绝。
- `McpAgentToolExecutor` 改用共享 schema validator，MCP 输入 schema 失败统一落 `agent_plan_schema_invalid`。
- 新增错误码：`planner_model_unavailable`、`agent_plan_invalid`、`agent_plan_tool_denied`、`agent_plan_schema_invalid`。

## 接口变化

- `/api/aigateway/agent/task/plan` 路由不变。
- `AgentTaskDto.planJson.steps[]` 兼容新增 `inputJson` 字段，前端不识别时不影响现有展示。
- language model usage 允许返回/接收 `"Planner"`。
- Tool Registry 和 MCP 执行接口路由不变。

## 验证命令

- `dotnet build AICopilot/src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj --no-restore`
- `dotnet test AICopilot/src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Suite=ToolRegistryGovernance|Suite=ModelSecretContract" --no-restore`
- `dotnet test AICopilot/src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
- `dotnet test AICopilot/src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj --no-restore`
- `dotnet test AICopilot/src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName!~FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload" --no-restore`
- `dotnet list AICopilot/src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj package --vulnerable --include-transitive`

## 当前结果

- HttpApi build：通过，0 warning。
- ToolRegistryGovernance + ModelSecretContract：24 项通过。
- ArchitectureTests：44 项通过。
- AiEvalTests：6 项通过。
- BackendTests 排除既有前端源码断言后的完整命令：两次超过 6 分钟未返回；使用 `--blame-hang-timeout 120s` 后仍超过 5 分钟未返回，需后续单独定位长跑或挂起用例。
- 漏洞检查：通过，未发现易受攻击包。

## 剩余风险

- 动态 Planner 仍是保守 MVP：模型只从后端提供的 allowlist 选工具，Cloud intent 仍由后端 CloudReadonly intent service 生成。
- JSON Schema 只覆盖确定性子集，没有引入第三方完整 schema 引擎。
- Runtime 仍保留现有内置工具行为；本批只把 `inputJson`、schema gate、动态 plan 接到统一治理链。
- CloudReadonly 工具仍默认 disabled；配方主数据和配方版本继续禁读。
