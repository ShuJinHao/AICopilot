# AICopilot 后端阶段记录 - McpServerBootstrap 拆分第一批 - 2026-06-01

## 本批目标

- 只修改 `AICopilot` 后端 Infrastructure MCP runtime 启动链路。
- 拆分 `McpServerBootstrap.cs`，将 client transport、approval policy、tool plugin 构建和 ToolRegistry projection 从 runtime 编排入口中分离。
- 保持 MCP candidate 条件、chat exposure 过滤、allowed tool 过滤、approval requirement、safety policy、runtime tool identity、ToolRegistry upsert、stdio argument parser、SSE timeout 和 endpoint 校验语义不变。
- 不修改公开 API/DTO、数据库迁移、部署编排、MCP 配置或真实 MCP 部署结构。

## 实际改动类别

- `McpServerBootstrap.cs` 保留 `StartAsync`、`ListCandidateServersAsync`、`CreateRegistrationAsync`、runtime candidate 判断、registration 生命周期和 protected virtual transport hook。
- 新增 `McpRuntimeClientFactory.cs`，承接 stdio/SSE client 创建、quoted argument parser、working directory resolve、SSE endpoint validator 和 connection timeout。
- 新增 `McpRuntimeProtectedToolReader.cs`，承接 approval requirement 读取与 protected tool name 计算。
- 新增 `McpRuntimeToolPluginBuilder.cs`，承接 MCP allowlist exposure、safety policy、annotation hint 读取、`AiToolDefinition` 和 `GenericBridgePlugin` 构建。
- 新增 `McpRuntimeToolRegistryProjection.cs`，承接 discovered tool registration projection、schema string 化和 ToolRegistry synchronizer 调用。
- 更新 `SecurityHardeningTests` 的 MCP runtime 源码扫描路径，组合读取 bootstrap 与 client factory 文件，不放松断言。

## 影响模块

- `AICopilot/src/infrastructure/AICopilot.Infrastructure/Mcp/`
- `AICopilot/src/tests/AICopilot.BackendTests/SecurityHardeningTests.cs`
- 影响能力：MCP runtime 文件组织。
- 不影响能力：MCP server 管理 API、ToolRegistry governance、Agent tool execution、deployment compose、Chiseled/npx 部署问题。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 API/DTO、数据库实体、迁移、EF mapping、部署编排或 MCP 配置。
- 未新增 NuGet/npm/容器依赖。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

- `dotnet build src/infrastructure/AICopilot.Infrastructure/AICopilot.Infrastructure.csproj --no-restore`
  - 结果：通过，0 warning / 0 error。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~McpServerBootstrapExposureTests|FullyQualifiedName~McpRuntimeRegistrySynchronizerTests|FullyQualifiedName~McpToolGovernanceTests|FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~ToolRegistryGovernanceTests|FullyQualifiedName~Phase25RuntimeSmokeTests"`
  - 结果：通过，131 passed / 0 failed。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
  - 结果：通过，759 passed / 0 failed。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
  - 结果：通过，44 passed / 0 failed。
- `git diff --check`
  - 结果：通过，无 whitespace error。
- `git diff --name-only`
  - 结果：通过；输出包含前序多批 tracked dirty 文件，本批 tracked 修改为 `McpServerBootstrap.cs` 与 `SecurityHardeningTests.cs`，新增文件通过 `git status --short --untracked-files=all` 核对。
- `wc -l src/infrastructure/AICopilot.Infrastructure/Mcp/*.cs`
  - 结果：`McpServerBootstrap.cs` 149 行；`McpRuntimeClientFactory.cs` 156 行；`McpRuntimeProtectedToolReader.cs` 47 行；`McpRuntimeToolPluginBuilder.cs` 173 行；`McpRuntimeToolRegistryProjection.cs` 43 行。

## 剩余风险

- 本批是 MCP runtime 文件组织拆分，未改变 MCP transport、approval、safety、ToolRegistry 或 runtime identity 行为。
- 当前工作树包含前序多批 AICopilot 拆分改动，本批只追加 MCP runtime bootstrap 拆分。
- MCP Chiseled 镜像缺 `npx` 的部署问题未在本批处理，仍需后续独立部署批次。

## 下一阶段进入条件

- `AICopilot.BackendTests` 与 `AICopilot.ArchitectureTests` 保持绿色。
- 后续可继续评估 500 行附近文件，或单独规划 MCP 独立容器部署修复。
- 如果后续需要改变 MCP transport 行为、ToolRegistry data boundary、approval/safety 策略、runtime tool identity、公开接口、数据库结构、配置绑定或部署配置，必须单独开批并重新确认范围。
