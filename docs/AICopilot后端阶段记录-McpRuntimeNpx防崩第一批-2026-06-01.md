# AICopilot 后端阶段记录 - McpRuntimeNpx 防崩第一批 - 2026-06-01

## 本批目标

- 只修改 `AICopilot` 后端 Infrastructure MCP runtime 与部署文档。
- 收口 Chiseled 镜像缺 `npx` 时 MCP stdio server 启动拖垮 `HttpApi` 的部署风险。
- 保持 MCP 管理 API、ToolRegistry、approval/safety 策略、runtime tool identity、SSE endpoint 校验、数据库结构和部署编排不变。

## 实际改动类别

- 新增 `McpRuntimeStdioCommandResolver`，在创建 stdio transport 前检查默认 `npx` 或配置的 `Command` 是否存在且可执行。
- 新增内部 `McpRuntimeStdioCommandUnavailableException`，仅表达 stdio command 不存在或不可执行。
- `McpServerBootstrap.CreateRegistrationAsync` 只捕获该内部异常，记录 warning 并跳过当前 MCP server registration；其他 runtime 错误继续抛出。
- 新增测试覆盖：缺失 command 时 `StartAsync` 不抛异常、不注册 plugin、返回 0 client；非 command-missing runtime 异常不被吞掉。
- 更新部署维护指南，将“数据库手动禁用 MCP”降级为最后兜底，并说明缺 `npx` 默认只跳过 stdio MCP server。

## 影响模块

- `AICopilot/src/infrastructure/AICopilot.Infrastructure/Mcp/`
- `AICopilot/src/tests/AICopilot.BackendTests/McpServerBootstrapExposureTests.cs`
- `AICopilot/src/tests/AICopilot.BackendTests/SecurityHardeningTests.cs`
- `AICopilot/AICopilot 项目部署与维护指南.md`

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 API/DTO、数据库实体、迁移、EF mapping、compose 服务结构、端口或 MCP 配置。
- 未新增 NuGet/npm/容器依赖。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~McpServerBootstrapExposureTests"`
  - 结果：通过，4 passed / 0 failed。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~McpServerBootstrapExposureTests|FullyQualifiedName~McpRuntimeRegistrySynchronizerTests|FullyQualifiedName~McpToolGovernanceTests|FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~ToolRegistryGovernanceTests|FullyQualifiedName~Phase25RuntimeSmokeTests"`
  - 结果：通过，133 passed / 0 failed。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
  - 结果：通过，761 passed / 0 failed。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
  - 结果：通过，44 passed / 0 failed。
- `git diff --check`
  - 结果：通过，无 whitespace error。
- `git diff --name-only`
  - 结果：通过；输出包含前序多批 tracked dirty 文件，本批 tracked 修改为 MCP runtime、MCP bootstrap 测试、安全扫描测试和部署维护指南，新增文件通过 `git status --short --untracked-files=all` 核对。
- `wc -l src/infrastructure/AICopilot.Infrastructure/Mcp/*.cs`
  - 结果：`McpServerBootstrap.cs` 164 行；`McpRuntimeClientFactory.cs` 158 行；`McpRuntimeStdioCommandResolver.cs` 117 行；其他 MCP runtime helper 均低于 500 行。

## 剩余风险

- 本批只做缺命令防崩，不解决 TypeScript MCP server 在 Chiseled 镜像内可用的问题。
- 完整 MCP 生产可用性仍建议后续通过独立 MCP sidecar/SSE endpoint 批次处理。
- 当前工作树包含前序多批 AICopilot 拆分改动，本批只追加 MCP runtime 防崩改动。

## 下一阶段进入条件

- `AICopilot.BackendTests` 与 `AICopilot.ArchitectureTests` 保持绿色。
- 后续 MCP 部署批次需单独规划 server 包、SSE endpoint、compose profile 和非破坏性迁移/配置步骤。
- 如果后续需要改变 MCP DB schema、MCP 管理 API、ToolRegistry data boundary、approval/safety 策略、compose 服务结构或容器镜像发布方式，必须单独开批并重新确认范围。
