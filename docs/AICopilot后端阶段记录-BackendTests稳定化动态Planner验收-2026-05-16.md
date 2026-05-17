# AICopilot 后端阶段记录：BackendTests 稳定化 + 动态 Planner 验收收口

日期：2026-05-16

## 改动范围

- 仅修改 `AICopilot` 后端、后端测试和后端阶段记录。
- 未修改 `src/vues`；当前前端 dirty 内容继续作为外部阻塞项记录。
- 未修改 `IIoT.CloudPlatform`、`IIoT.EdgeClient`，未新增 Cloud 项目引用，未直接访问 Cloud 业务库。

## 本批改动

- 定位 BackendTests 超时根因：非 Docker 后端测试 420 项约 1 分 20 秒通过；`Runtime=DockerRequired` 测试单独约 5 分 20 秒完成。之前超时不是测试挂死，而是完整命令包含 Docker/Aspire 集成测试，外部命令超时时间设置过短。
- `AgentTaskDto.planJson` 兼容新增只读元数据：`plannerMode`、`plannerModelId`、`plannerValidationVersion`。
- `DefaultAgentDynamicPlanner` 增强非法输出治理：空响应、Markdown 包裹 JSON、超长响应、未知字段、非对象或超长 `inputJson` 都返回稳定错误。
- Planner 输入摘要统一脱敏：goal、tool 描述、target、schema 摘要不暴露 API Key、token、连接串、服务器绝对路径、SQL/表名。
- `ToolInputSchemaValidator` 保持 `System.Text.Json` 子集实现，补齐嵌套对象、数组 items、required 字段路径的确定性错误消息。
- 扩展后端测试覆盖：动态 Planner 非法输出、Planner 输入脱敏、Plan Guard 工具拒绝、MCP runtime availability、schema required/type/enum/array 校验、plan 元数据落盘。

## 接口变化

- `/api/aigateway/agent/task/plan` 路由不变。
- `AgentTaskDto.planJson` 兼容新增字段：
  - `plannerMode`: `Static` 或 `Dynamic`
  - `plannerModelId`: 动态 Planner 模型 id，静态模式为 `null`
  - `plannerValidationVersion`: 当前为 `1`
- `planJson.steps[].inputJson` 继续保留。
- 继续使用既有错误码：`planner_model_unavailable`、`agent_plan_invalid`、`agent_plan_tool_denied`、`agent_plan_schema_invalid`。

## 验证命令

- `dotnet build AICopilot/src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj --no-restore`
- `dotnet test AICopilot/src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Suite=ToolRegistryGovernance|Suite=DynamicPlannerContract|Suite=ModelSecretContract" --no-restore`
- `dotnet test AICopilot/src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Runtime!=DockerRequired&FullyQualifiedName!~FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload" --no-build`
- `dotnet test AICopilot/src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Runtime=DockerRequired" --no-build`
- `dotnet test AICopilot/src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName!~FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload" --no-build`
- `dotnet test AICopilot/src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
- `dotnet test AICopilot/src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj --no-restore`
- `dotnet list AICopilot/src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj package --vulnerable --include-transitive`

## 当前结果

- HttpApi build：通过，0 warning。
- Planner/ToolRegistry/Secret 合并过滤：33 项通过。
- 非 Docker BackendTests：420 项通过，约 1 分 20 秒。
- DockerRequired BackendTests：通过，约 5 分 20 秒。
- BackendTests 排除既有前端源码断言后的完整命令：468 项通过，4 分 43 秒。
- ArchitectureTests：44 项通过。
- AiEvalTests：6 项通过。
- 漏洞检查：通过，未发现易受攻击包。

## 剩余风险

- 完整 BackendTests 依赖 Docker/Aspire 集成环境，单次运行需要约 5 分钟；后续验证命令需要预留足够超时时间。
- 动态 Planner 仍是保守 MVP：模型只从后端提供的 allowlist 选工具，Cloud intent 仍由后端 CloudReadonly intent service 生成。
- JSON Schema 仍为确定性子集实现，未引入完整第三方 schema 引擎。
- CloudReadonly 工具仍默认 disabled；配方主数据和配方版本继续禁读。
