# AICopilot 后端阶段记录 - AgentTaskRuntime 拆分第一批 - 2026-05-27

## 本批目标

- 只在 `AICopilot` 内收口 `AgentTaskRuntime.cs` 过长问题。
- 保持 `IAgentTaskRuntime.RunAsync` 外部契约、审批状态机、CloudReadonly 只读边界、ToolExecution 审计语义和数据库结构不变。
- 本批不处理 MCP 独立容器部署，不拆第二层大文件，不修改 Cloud/Edge。

## 实际改动类别

- 将 run attempt 创建、恢复、lease 获取和刷新提取到 `AgentTaskRunAttemptCoordinator`。
- 将 runtime 内置工具执行分发提取到 `AgentBuiltInToolDispatcher`。
- 将 chart/report/pdf/pptx/xlsx Artifact 草稿生成提取到 `AgentRuntimeArtifactBuilder`。
- 将 ToolExecution 输入摘要、输出摘要、audit metadata、错误码解析和敏感信息裁剪提取到 `AgentToolExecutionAuditBuilder`。
- 将 runtime JSON 配置和 `AgentTaskRunState` 相关 internal record 拆到独立文件。
- 将源码扫描测试更新到新的 RAG 权限复核位置：`AgentBuiltInToolDispatcher.cs`。

## 影响模块

- `src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskRuntime.cs`
- `src/services/AICopilot.AiGatewayService/AgentTasks/Runtime/*.cs`
- `src/tests/AICopilot.BackendTests/SecurityHardeningTests.cs`

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 contracts、DTO、数据库迁移、容器编排、部署配置或 MCP server 配置。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~ToolRegistryGovernanceTests|FullyQualifiedName~AgentRunQueueProductionOpsTests|FullyQualifiedName~EnterpriseCloudReadonly"`：通过，127/127。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`：通过，759/759。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`：通过，44/44。

## 剩余风险

- `AgentBuiltInToolDispatcher.cs` 仍接近 1000 行，本批只是从主 runtime 中剥离，不是最终结构。
- CloudReadonly 试运行、Text-to-SQL、RAG 和 Artifact 生成仍集中在同一个 built-in dispatcher，下一批可以继续按能力拆分。
- 本批没有处理 MCP chiseled 镜像缺 `npx` 的部署风险。

## 下一阶段进入条件

- 继续拆 `AgentBuiltInToolDispatcher`，优先按 CloudReadonly 查询、上传/表格解析、RAG 检索、Business Text-to-SQL 四类能力拆分。
- 或单独开启 MCP 独立容器部署批次，明确 MCP server 包、SSE endpoint、compose profile 和非破坏性配置迁移步骤。
- 每个后续批次继续保持只改 `AICopilot`，除非另有当前轮明确跨项目授权。
