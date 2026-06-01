# AICopilot 后端阶段记录 - AgentBuiltInToolDispatcher 拆分第一批 - 2026-05-27

## 本批目标

- 只在 `AICopilot` 内继续收口 Agent runtime 结构债。
- 将 `AgentBuiltInToolDispatcher.cs` 从近千行拆成薄分发器和按能力划分的 internal runtime service。
- 保持 `IAgentTaskRuntime`、审批状态机、CloudReadonly 只读边界、ToolExecution audit 语义、数据库结构和部署配置不变。

## 实际改动类别

- 新增 `AgentRuntimeFileInputToolService`，承接上传文件读取、表格解析、CSV/preview/file-name helper。
- 新增 `AgentRuntimeRagToolService`，承接 `rag_search`、知识库权限复核、task owner admin 判断。
- 新增 `AgentRuntimeCloudReadonlyToolService`，承接 CloudReadonly、sandbox、controlled sandbox、production pilot、controlled production pilot 查询路径。
- 新增 `AgentRuntimeBusinessQueryToolService`，承接 Business Text-to-SQL 查询和 business query summary。
- 将 `AgentBuiltInToolDispatcher` 收敛为 tool code switch 和协作者调用。
- 将 `SecurityHardeningTests` 的 RAG 权限源码扫描位置更新到 `AgentRuntimeRagToolService.cs`。

## 影响模块

- `src/services/AICopilot.AiGatewayService/AgentTasks/Runtime/AgentBuiltInToolDispatcher.cs`
- `src/services/AICopilot.AiGatewayService/AgentTasks/Runtime/AgentRuntime*ToolService.cs`
- `src/tests/AICopilot.BackendTests/SecurityHardeningTests.cs`

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 contracts、DTO、数据库迁移、容器编排、MCP 配置或部署配置。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~ToolRegistryGovernanceTests|FullyQualifiedName~AgentRunQueueProductionOpsTests|FullyQualifiedName~EnterpriseCloudReadonly|FullyQualifiedName~SecurityHardeningTests"`：通过，185/185。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`：通过，759/759。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`：通过，44/44。

## 剩余风险

- `AgentRuntimeCloudReadonlyToolService.cs` 仍有 400 行以上，但低于 500 行；后续可按 sandbox、production pilot 再拆。
- 本批未处理 MCP chiseled 镜像缺 `npx` 的部署风险。
- 本批未做 GitHub review/commit/PR 流程。

## 下一阶段进入条件

- 可继续拆 `AgentRuntimeCloudReadonlyToolService`，优先分离 sandbox trial 与 production pilot 两类路径。
- 或单独开启 MCP 独立容器部署批次，明确 MCP server 包、SSE endpoint、compose profile 和非破坏性配置迁移步骤。
- 后续批次继续默认只改 `AICopilot`，除非当前轮另有明确跨项目授权。
