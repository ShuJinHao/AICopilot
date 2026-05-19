# A助理 Agent Runtime MCP 工具发现与治理 Batch 9 验收报告

生成时间：2026-05-18

## 范围

本批只实现 AICopilot 后端的 MCP 工具治理只读视图，范围包含后端服务、HTTP API、后端测试和本验收文档。未修改 Cloud、Edge、前端 `src/vues`，未新增依赖，未新增数据库迁移，未接入真实 Cloud。

## 完成内容

- 新增 `IMcpToolRegistryReadService` 只读契约和 `McpToolRegistryReadModel`，放在 `AICopilot.Services.Contracts`，用于跨服务读取已同步的 MCP ToolRegistration。
- 在 `AICopilot.AiGatewayService` 实现 `McpToolRegistryReadService`，只读取现有 ToolRegistry 数据，并用 `IAgentPluginCatalog` 判断 runtime 当前是否可用。
- 在 `AICopilot.McpService` 新增 `GetMcpToolGovernanceQuery` 和治理 DTO：
  - `McpToolGovernanceDto`
  - `McpToolGovernanceSummaryDto`
  - `McpToolGovernancePageDto`
- 新增后端接口：`GET /api/mcp/tool-governance`。
- 查询支持 `serverId`、`serverName`、`status`、`includeOrphans` 筛选。
- 输出治理状态：
  - `AllowlistedOnly`
  - `RegisteredDisabled`
  - `Ready`
  - `RuntimeUnavailable`
  - `OrphanedRegistration`
  - `Blocked`
- 权限要求为同时具备：
  - `Mcp.GetListServers`
  - `AiGateway.ToolRegistry.Read`
- 治理动作继续复用现有 MCP server update 和 Tool Registry PATCH，本批未新增手动发现、同步或执行接口。

## 边界确认

- 未主动连接外部 MCP server。
- 未调用 `ListToolsAsync`。
- 未执行 MCP 工具。
- 未开放 shell。
- 未修改 Cloud、Edge、前端 `src/vues`。
- 未访问真实 Cloud。
- 未新增 NuGet/npm 依赖。
- 未新增数据库迁移或数据库字段。
- MCP discovered tool 既有默认安全策略未改变：默认 disabled、requires approval，Planner/Runtime 仍只使用已注册、已启用、runtime available、权限满足且未 blocked 的工具。

## 验证结果

- `dotnet build .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug`
  - 通过：0 warning，0 error。
- `dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=Batch9McpToolGovernance"`
  - 通过：4/4。
- `dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=ToolRegistryGovernance|Suite=Phase43SafetyQuality"`
  - 通过：105/105。
- `dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=Batch5ApprovalHardening|Suite=Batch6SecretProtection|Suite=Batch7ReportArtifacts|Suite=Batch8ArtifactVersioning"`
  - 通过：15/15。
- `.\scripts\Run-AgentSimulationAcceptance.ps1`
  - 通过：scope guard、后端 build、Simulation unit tests 3/3、Simulation Docker acceptance 1/1。

## 影响确认

- Cloud：未修改。
- Edge：未修改。
- 前端 `src/vues`：未修改本批文件，既有 dirty 状态保持不动。
- 真实 MCP：未连接、未发现、未执行。
- 真实 Cloud：未访问、未新增真实 Cloud 调用。
- shell 能力：未新增。
- 任意路径写入：未新增。

## 剩余风险

- 本批治理视图只呈现已配置 allowlist、已同步 ToolRegistration 和当前 runtime catalog 的离线状态；不会主动发现外部 MCP server 的实时工具清单。
- `RuntimeUnavailable` 只能说明 ToolRegistry 中存在但当前 runtime catalog 不可用，具体原因仍需后续通过 MCP server 配置、同步链路或运行时日志定位。
