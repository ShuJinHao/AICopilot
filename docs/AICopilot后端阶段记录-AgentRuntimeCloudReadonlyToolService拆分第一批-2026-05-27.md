# AICopilot 后端阶段记录 - AgentRuntimeCloudReadonlyToolService 拆分第一批 - 2026-05-27

## 本批目标

- 只在 `AICopilot` 内继续收口 Agent runtime 结构债。
- 将 `AgentRuntimeCloudReadonlyToolService.cs` 从 462 行拆成薄门面和按 CloudReadonly 能力划分的 internal runtime service。
- 保持 `IAgentTaskRuntime`、审批状态机、CloudReadonly 只读边界、ToolExecution audit 语义、返回 JSON、数据库结构和部署配置不变。

## 实际改动类别

- 新增 `AgentRuntimeCloudReadonlyBasicToolService`，承接 `query_cloud_data_readonly` 基础只读查询。
- 新增 `AgentRuntimeCloudReadonlySandboxToolService`，承接 sandbox 固定场景和 controlled sandbox 查询。
- 新增 `AgentRuntimeCloudReadonlyProductionPilotToolService`，承接 P12 production pilot 和 P13 controlled production pilot 查询。
- 新增 `AgentRuntimeStepInputReader`，承接 step input JSON 解析和 result error summary helper。
- 将 `AgentRuntimeCloudReadonlyToolService` 收敛为 CloudReadonly runtime 薄门面，继续由 `AgentBuiltInToolDispatcher` 调用。

## 影响模块

- `src/services/AICopilot.AiGatewayService/AgentTasks/Runtime/AgentRuntimeCloudReadonlyToolService.cs`
- `src/services/AICopilot.AiGatewayService/AgentTasks/Runtime/AgentRuntimeCloudReadonly*ToolService.cs`
- `src/services/AICopilot.AiGatewayService/AgentTasks/Runtime/AgentRuntimeStepInputReader.cs`

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 contracts、DTO、数据库迁移、容器编排、MCP 配置或部署配置。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~EnterpriseCloudReadonly|FullyQualifiedName~ToolRegistryGovernanceTests|FullyQualifiedName~SecurityHardeningTests"`：通过，178/178。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`：通过，759/759。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`：通过，44/44。

## 剩余风险

- 本批只拆 Agent runtime 内部结构，没有处理 MCP chiseled 镜像缺 `npx` 的部署风险。
- 本批未做 GitHub review/commit/PR 流程。
- `CloudReadiness` 下的 production operations 和 production pilot 服务仍有较大文件，属于后续独立结构债。

## 下一阶段进入条件

- 可继续拆 `AgentRuntimeArtifactBuilder` 或 `AgentToolExecutionAuditBuilder`，保持 Agent runtime 单文件均低于 500 行。
- 或单独开启 `CloudReadiness` production operations 结构收口批次，前提是仍只改 `AICopilot` 且不改变审批和只读边界。
- MCP 独立容器部署修复仍应作为单独部署批次处理。
