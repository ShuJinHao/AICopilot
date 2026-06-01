# AICopilot 后端阶段记录 - AiGatewayController 拆分第一批 - 2026-05-28

## 本批目标

- 只修改 `AICopilot` 后端 Host 层。
- 拆分 `AiGatewayController.cs`，将 931 行 Controller 单体收敛为 `/api/aigateway` 下的薄入口 controller 组合。
- 保持 `/api/aigateway` 路由前缀、HTTP verb、route template、request/response wire shape、SSE 返回方式和 artifact `File(...)` 返回语义不变。
- 不改变公开 API/DTO、权限策略、数据库结构、部署编排或 MCP 配置。

## 实际改动类别

- `AiGatewayController.cs` 保留为基础配置与模型治理入口：language model、routing model、provider reliability、model pool、runtime settings、prompt policy、conversation template、approval policy。
- 新增 `AiGatewayToolController.cs`：承接 tools、tool catalog、tool detail/update 和 tool definition activate/disable。
- 新增 `AiGatewaySessionController.cs`：承接 session、chat message、chat SSE、approval decision SSE 和 pending approval。
- 新增 `AiGatewayAgentTaskController.cs`：承接 agent task、agent approval、run queue、worker status、trial scenario、CloudReadonly sandbox/production controlled plan。
- 新增 `AiGatewayTrialPilotController.cs`：承接 trial operations 与 pilot authorization。
- 新增 `AiGatewayWorkspaceArtifactController.cs`：承接 upload、artifact workspace、artifact download/content/preview/versioning。
- 新增 `AiGatewayControllerRequests.cs`：承接原 controller 底部 request records，字段、默认值和名称保持不变。
- 更新源码扫描测试：从单文件读取 `AiGatewayController.cs` 改为组合读取 `AiGateway*.cs`，并继续断言 controller `[Authorize]` 与 `ISender` 构造注入。

## 影响模块

- `AICopilot/src/hosts/AICopilot.HttpApi/Controllers/`
- `AICopilot/src/tests/AICopilot.BackendTests/`
- 影响能力：AICopilot HttpApi Host 层 controller 文件组织。
- 不影响能力：业务 command/query、service 层行为、数据库、部署、MCP、前端页面。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 contracts/DTO。
- 未修改数据库迁移、容器编排、MCP 配置。
- 未新增 NuGet/npm/容器依赖。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~FrontendIntegrationContractTests|FullyQualifiedName~AICopilotM2_2ReadinessScopeTests|FullyQualifiedName~AICopilotM2M9GovernanceScopeTests|FullyQualifiedName~EnterpriseAgentWorkbenchP2Tests|FullyQualifiedName~EnterpriseArtifactWorkspaceP9Tests|FullyQualifiedName~AgentApprovalPermissionHardeningTests|FullyQualifiedName~ToolRegistryGovernanceTests|FullyQualifiedName~TrialOperations"`
  - 结果：通过 122/122。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
  - 结果：通过 759/759。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
  - 结果：通过 44/44。
- `git diff --check`
  - 结果：通过，无 whitespace error 输出。
- `git diff --name-only`
  - 结果：当前工作树包含前序多批拆分与安全收口改动；本批实际新增/修改文件限定在 AICopilot HttpApi Controller 拆分文件、相关源码扫描测试和本阶段记录。
- `wc -l src/hosts/AICopilot.HttpApi/Controllers/AiGateway*.cs`
  - `AiGatewayAgentTaskController.cs`：193 行。
  - `AiGatewayController.cs`：217 行。
  - `AiGatewayControllerRequests.cs`：41 行。
  - `AiGatewaySessionController.cs`：78 行。
  - `AiGatewayToolController.cs`：72 行。
  - `AiGatewayTrialPilotController.cs`：212 行。
  - `AiGatewayWorkspaceArtifactController.cs`：171 行。

## 剩余风险

- 本批是搬移式拆分，未新增 API，也未改变 controller 到 MediatR command/query 的转发语义。
- 当前验证覆盖了 Controller 安全扫描、前端集成契约、M2/M9 治理范围、Agent Workbench、Artifact Workspace P9、审批权限、ToolRegistry 和 TrialOperations 相关链路。
- 未做浏览器或前端实机验证；本批没有改 `AICopilot/src/vues/**`，以前端集成契约测试作为接口形状验证。

## 下一阶段进入条件

- 继续从 `AICopilot.BackendTests 759/759` 与 `ArchitectureTests 44/44` 绿色基线进入下一批。
- 下一批可优先评估仍超过 500 行、边界清楚且测试覆盖集中的 AICopilot 后端文件。
- 若后续涉及公开 API、权限策略、SSE 返回方式、artifact 文件返回、数据库结构、部署或 MCP 配置，必须单独开批并重新确认范围。
