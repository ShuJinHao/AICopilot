# AICopilot 后端阶段记录 - HttpApiDependencyInjection 拆分第一批 - 2026-06-01

## 本批目标

- 只修改 `AICopilot` 后端 Host 层。
- 拆分 `AICopilot.HttpApi/DependencyInjection.cs`，将 options 配置校验、认证配置和 rate limiting 配置从组合根入口中分离。
- 保持 `DependencyInjection` 类型、`AddApplicationService()`、`AddWebServices()` 入口和外部调用语义不变。
- 不修改 HTTP API、DTO、数据库迁移、部署编排、MCP 配置或运行行为。

## 实际改动类别

- `DependencyInjection.cs` 收敛为薄入口，继续负责应用服务注册和 Web 服务组合。
- 新增 `HttpApiOptionsConfiguration.cs`，承接 `JwtSettings`、Cloud OIDC、CloudIdentityStatus、CloudReadonly、CloudAiRead 的 options 注册与 `EnsureValid` 校验。
- 新增 `HttpApiAuthenticationConfiguration.cs`，承接 Cookie、OIDC、JWT Bearer、security stamp、Cloud identity status 和 401 problem response 配置。
- 新增 `HttpApiRateLimitingConfiguration.cs`，承接 default/login/chat/identity-management rate limiter、partition key、login username 读取和 429 problem response。
- 更新 `SecurityHardeningTests` 中 login rate limiter 源码扫描路径，改为组合读取 `DependencyInjection.cs` 和 `HttpApiRateLimitingConfiguration.cs`，不放松断言。

## 影响模块

- `AICopilot/src/hosts/AICopilot.HttpApi/`
- `AICopilot/src/tests/AICopilot.BackendTests/SecurityHardeningTests.cs`
- 影响能力：Host 组合根文件组织。
- 不影响能力：JWT/OIDC 行为、Cloud identity status 校验、限流策略、problem details wire shape、Controller 路由、数据库和部署配置。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 API/DTO、数据库实体、迁移、EF mapping、部署编排或 MCP 配置。
- 未新增 NuGet/npm/容器依赖。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

- `dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj --no-restore`
  - 结果：通过，0 warning / 0 error。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~CloudOidcLoginTests|FullyQualifiedName~IdentityAccessManagementTests|FullyQualifiedName~FreshDatabaseSeedTests|FullyQualifiedName~FrontendIntegrationContractTests"`
  - 结果：通过，92 passed / 0 failed。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
  - 结果：通过，759 passed / 0 failed。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
  - 结果：通过，44 passed / 0 failed。
- `git diff --check`
  - 结果：通过，无 whitespace error。
- `git diff --name-only`
  - 结果：仅列出当前 AICopilot 工作树内 tracked diff；工作树仍包含前序多批 AICopilot 拆分改动。本批新增 helper 文件和本阶段记录为 untracked 文件，需结合 `git status --short --untracked-files=all` 审查。
- `wc -l src/hosts/AICopilot.HttpApi/DependencyInjection.cs src/hosts/AICopilot.HttpApi/HttpApi*.cs`
  - 结果：`DependencyInjection.cs` 42 行，`HttpApiAuthenticationConfiguration.cs` 258 行，`HttpApiOptionsConfiguration.cs` 106 行，`HttpApiRateLimitingConfiguration.cs` 222 行，单文件均低于 500 行。

## 剩余风险

- 本批是 Host 组合根结构拆分，未改变认证、授权、限流或配置语义。
- 当前工作树包含前序多批 AICopilot 拆分改动，本批只追加 HttpApi DependencyInjection 拆分。
- 未做真实部署演练；运行行为由 BackendTests 全量和 ArchitectureTests 覆盖。

## 下一阶段进入条件

- `AICopilot.BackendTests` 与 `AICopilot.ArchitectureTests` 已保持绿色，可进入下一批。
- 后续可继续评估 `PilotAuthorizationSubmission.cs`、`SemanticSummaryProfiles.cs` 或 `McpServerBootstrap.cs` 等剩余大文件。
- 如果后续需要改变 JWT/OIDC/rate limiting 行为、配置结构、401/429 problem details、公开接口、数据库结构或部署配置，必须单独开批并重新确认范围。
