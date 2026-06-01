# AICopilot 后端阶段记录 - CloudReadonlyPilotReadiness 拆分第一批 - 2026-05-27

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `CloudReadonlyPilotReadiness.cs`，降低 P11 Pilot readiness 单文件职责密度。
- 保持 P10 evidence gate、P11 config package、approval rehearsal、contract rehearsal、ToolRegistry 保护和 explicit audit save 语义不变。
- 不新增功能，不改 MCP 部署，不触碰 Cloud/Edge/UI/数据库迁移/部署编排。

## 实际改动

- `CloudReadonlyPilotReadiness.cs` 收敛为 contracts 文件，仅保留状态常量、DTO、commands、queries 和 store interface。
- 新增 `CloudReadonlyPilotReadinessHandlers.cs`，承接 status query、create package、gate evaluation、approval rehearsal、contract rehearsal handlers，并保留显式 audit write/save。
- 新增 `CloudReadonlyPilotReadinessStores.cs`，承接 `InMemoryCloudReadonlyPilotReadinessStore`，保留 20 个 package 保留口径。
- 新增 `CloudReadonlyPilotReadinessService.cs`，承接 BuildStatus、EvaluateGate、CreatePackage、RunApprovalRehearsal、RunContractRehearsal 主编排。
- 新增 `CloudReadonlyPilotReadinessPolicy.cs`，承接 production boundary、ToolRegistry protected tool 校验、allowed endpoint/text normalize。
- 新增 `CloudReadonlyPilotReadinessContractRehearsal.cs`，承接 endpoint specs、fake contract check、contract summary、approval rehearsal step 和 hash helper。
- 更新 `SecurityHardeningTests` explicit audit save 白名单路径到 `CloudReadonlyPilotReadinessHandlers.cs`，未放松断言。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.AiGatewayService/CloudReadiness`
- 能力边界：P11 Pilot readiness 内部结构拆分。
- 公开契约：未修改 DTO 字段、command/query 类型、permission attributes、status 字符串、marker 常量、store interface、service 构造参数和 public 方法签名。
- 运行行为：未修改 P10/P11 gate、ToolRegistry data boundary、audit payload、config package、approval rehearsal、contract rehearsal 和 fake endpoint 行为。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改数据库迁移、部署编排、MCP 配置、公开 DTO/API 语义。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~EnterpriseCloudReadonlyPilotReadinessP11Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionPilotP12Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionControlledPilotP13Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionOperationsP14Tests|FullyQualifiedName~EnterpriseProductionPilotHardeningP16Tests|FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~ToolRegistryGovernanceTests"
```

- 结果：通过 139 / 139，失败 0，跳过 0。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 41 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0。

```bash
wc -l src/services/AICopilot.AiGatewayService/CloudReadiness/*PilotReadiness*.cs
```

- `CloudReadonlyPilotReadiness.cs`：121 行。
- `CloudReadonlyPilotReadinessContractRehearsal.cs`：166 行。
- `CloudReadonlyPilotReadinessHandlers.cs`：212 行。
- `CloudReadonlyPilotReadinessPolicy.cs`：122 行。
- `CloudReadonlyPilotReadinessService.cs`：218 行。
- `CloudReadonlyPilotReadinessStores.cs`：76 行。

## 剩余风险

- 本批只做 P11 Pilot readiness 结构拆分，没有处理仍超过 500 行的其他 CloudReadiness 文件。
- `CloudReadonlyReadiness.cs` 和 `CloudReadonlySandboxAgentTrial.cs` 仍是后续结构债候选。
- MCP 独立容器、部署配置修复和 Runtime 进一步拆分不在本批范围内。

## 下一阶段进入条件

- 从当前绿色基线继续：`BackendTests 759/759`、`ArchitectureTests 44/44`。
- 下一批建议优先拆 `CloudReadonlyReadiness.cs`，按 contracts/store/service/handlers/policy/check runner 的边界继续收敛 P5/P6 readiness。
- 若进入 MCP 部署修复，必须单独规划 MCP server 包、SSE endpoint、compose profile 和非破坏性配置迁移步骤。
