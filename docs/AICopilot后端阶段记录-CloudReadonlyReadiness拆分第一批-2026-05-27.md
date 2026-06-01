# AICopilot 后端阶段记录 - CloudReadonlyReadiness 拆分第一批 - 2026-05-27

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `CloudReadonlyReadiness.cs`，降低 P5/P6 readiness 与 sandbox smoke 单文件职责密度。
- 保持 DryRun、FakeEndpoint、RealSandboxSmoke、ToolRegistry 默认关闭防护、sandbox-only 调用、错误映射、hash 和返回 JSON 脱敏语义不变。
- 不新增功能，不改 MCP 部署，不触碰 Cloud/Edge/UI/数据库迁移/部署编排。

## 实际改动

- `CloudReadonlyReadiness.cs` 收敛为 contracts 文件，仅保留 modes、statuses、DTO、queries、command 和 history store interface。
- 新增 `CloudReadonlyReadinessHandlers.cs`，承接 current/history/sandbox status/sandbox smoke history/run readiness handlers。
- 新增 `CloudReadonlyReadinessStores.cs`，承接 `InMemoryCloudReadonlyReadinessHistoryStore`，保留 20 条 history 保留口径。
- 新增 `CloudReadonlyReadinessService.cs`，承接 `NormalizeMode`、`BuildCurrent`、`BuildSandboxStatus`、`RunAsync` 主编排。
- 新增 `CloudReadonlyReadinessPolicy.cs`，承接 readiness/sandbox 配置校验、ToolRegistry 默认关闭校验和 status resolve。
- 新增 `CloudReadonlyReadinessEndpointCatalog.cs`，承接 endpoint specs、default endpoint codes、dry-run/fake/skipped check、模拟失败映射和 hash helper。
- 新增 `CloudReadonlySandboxSmokeRunner.cs`，承接 real sandbox smoke 调用、smoke query、row count、HTTP/JSON/timeout 错误映射。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.AiGatewayService/CloudReadiness`
- 能力边界：P5/P6 CloudReadonly readiness 内部结构拆分。
- 公开契约：未修改 DTO 字段、query/command 类型、permission attributes、status/mode 字符串、history store interface、service 构造参数和 public 方法签名。
- 运行行为：未修改 DryRun、FakeEndpoint、RealSandboxSmoke、ToolRegistry 默认关闭防护、sandbox-only 调用边界、错误映射和 hash 生成口径。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改数据库迁移、部署编排、MCP 配置、公开 DTO/API 语义。
- 未创建分支、worktree、commit 或 PR。
- 未修改 `SecurityHardeningTests`；本批没有 explicit audit save 白名单迁移需求。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~EnterpriseCloudReadonlyReadinessP5Tests|FullyQualifiedName~EnterpriseCloudReadonlySandboxP6Tests|FullyQualifiedName~EnterpriseCloudReadonlySandboxAgentTrialP7Tests|FullyQualifiedName~EnterpriseCloudReadonlySandboxExpansionP8Tests|FullyQualifiedName~ToolRegistryGovernanceTests|FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~FrontendIntegrationContractTests"
```

- 结果：通过 148 / 148，失败 0，跳过 0。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 43 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0。

```bash
wc -l src/services/AICopilot.AiGatewayService/CloudReadiness/*Readiness*.cs
```

- P5/P6 新拆分文件：`CloudReadonlyReadiness.cs` 89 行、`EndpointCatalog` 219 行、`Handlers` 88 行、`Policy` 233 行、`Service` 182 行、`Stores` 27 行。
- 既有 P11 `*PilotReadiness*.cs` 文件仍全部低于 500 行。
- `CloudReadonlySandboxSmokeRunner.cs`：162 行。

## 剩余风险

- 本批只做 P5/P6 readiness 结构拆分，没有处理 `CloudReadonlySandboxAgentTrial.cs`。
- `CloudReadonlySandboxAgentTrial.cs` 仍为 564 行，是下一批结构债候选。
- MCP 独立容器、部署配置修复和 Runtime 进一步拆分不在本批范围内。

## 下一阶段进入条件

- 从当前绿色基线继续：`BackendTests 759/759`、`ArchitectureTests 44/44`。
- 下一批建议优先拆 `CloudReadonlySandboxAgentTrial.cs`，按 contracts/store/service/handlers/scenario/query projection 的边界继续收敛 P7 fixed sandbox trial。
- 若进入 MCP 部署修复，必须单独规划 MCP server 包、SSE endpoint、compose profile 和非破坏性配置迁移步骤。
