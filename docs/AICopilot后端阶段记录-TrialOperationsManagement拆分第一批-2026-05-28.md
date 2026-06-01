# AICopilot 后端阶段记录 - TrialOperationsManagement 拆分第一批 - 2026-05-28

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `TrialOperationsManagement.cs`，把 trial campaign query/command handlers、DTO mapping、task evidence、pilot readiness evaluator 和 evidence package builder 从单体文件中剥离。
- 保持 TrialOperations permissions、DTO、query、command records、public handler 构造参数和 `Handle` 签名不变。
- 不新增 trial 功能，不改前端契约，不改数据库、部署编排或 MCP 配置。

## 实际改动

- `TrialOperationsManagement.cs` 收敛为 contracts/入口文件，仅保留 permissions、DTO、queries 和 commands。
- 新增 `TrialCampaignQueryHandlers.cs`，承接 trial campaign list/detail 查询。
- 新增 `TrialCampaignCommandHandlers.cs`，承接 campaign create/status update/task attach/risk upsert/readiness evaluation/evidence package command handlers，并保留 explicit audit write/save。
- 新增 `TrialOperationsMapper.cs`，承接 campaign、run、risk 和 summary DTO 映射。
- 新增 `TrialTaskEvidenceReader.cs`，承接 agent task artifact evidence、source mode、query/result hash 和 final approval/run status 解析。
- 新增 `PilotReadinessEvaluator.cs`，承接 P10/P11 readiness checks、blocker/warning 和 metrics 计算。
- 新增 `TrialEvidencePackageBuilder.cs`，承接 evidence package metrics、items 和 unresolved risks 汇总。
- 更新 `SecurityHardeningTests` explicit audit save 白名单，从旧单体文件迁移到实际包含 `auditLogWriter.SaveChangesAsync` 的 command handler 文件。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.AiGatewayService/TrialOperations`
- 能力边界：P10 trial operations campaign、scenario evidence attach、risk register、pilot readiness evaluation、evidence package 内部结构拆分。
- 公开契约：未修改 DTO 字段、queries、commands、permission attributes、status 字符串、repository specs、数据库实体、数据库迁移或 DI 注册语义。
- 运行行为：未修改 campaign 创建/状态更新/任务挂载/risk upsert/readiness evaluation/evidence package、audit payload、证据 hash、source metadata 或权限语义。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改数据库迁移、部署编排或 MCP 配置。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~TrialOperations|FullyQualifiedName~EnterpriseProductionPilotHardeningP16Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionOperationsP14Tests|FullyQualifiedName~AgentRunQueueProductionOpsTests|FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~FrontendIntegrationContractTests|FullyQualifiedName~AcceptanceClosureVerificationTests"
```

- 结果：通过 84 / 84，失败 0，跳过 0，耗时 54 s。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 43 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0，耗时 371 ms。

```bash
wc -l src/services/AICopilot.AiGatewayService/TrialOperations/*.cs
```

- `TrialOperationsManagement.cs`：160 行。
- `TrialCampaignCommandHandlers.cs`：340 行。
- `TrialCampaignQueryHandlers.cs`：39 行。
- `TrialOperationsMapper.cs`：93 行。
- `TrialTaskEvidenceReader.cs`：114 行。
- `PilotReadinessEvaluator.cs`：133 行。
- `TrialEvidencePackageBuilder.cs`：46 行。

```bash
git diff --check
```

- 结果：通过，无 whitespace 错误输出。

```bash
git diff --name-only
```

- 结果：输出均为 `AICopilot` 路径，未出现 `IIoT.CloudPlatform/**`、`IIoT.EdgeClient/**` 或 `AICopilot/src/vues/**`。
- 说明：该命令不列未跟踪新增文件；本批新增 split 文件和阶段记录见“实际改动”。

## 剩余风险

- 本批只处理 `TrialOperationsManagement.cs`，没有拆 controller、core domain 或 infrastructure runtime reliability 文件。
- `TrialCampaignCommandHandlers.cs` 保留多个 command handler，但单文件仍低于 500 行；后续只有在 TrialOperations 继续扩展时才需要继续按 command 类型拆分。
- `SecurityHardeningTests.cs` 本轮只迁移 explicit audit save 白名单路径，未放松断言。

## 下一阶段进入条件

- 需要保持当前绿色基线：`BackendTests` 全量通过、`ArchitectureTests` 全量通过。
- 下一批建议在单独计划中评估 `ModelProviderReliability.cs`、`AiGatewayController.cs`、`ProductionPilotOperations.cs` 或 `AgentArtifactDocumentServices.cs`，按风险优先选择。
- 如果下一批涉及 Trial API、权限策略、readiness gate、audit save 边界、evidence/hash/source metadata 语义、公开接口、数据库结构或部署配置，必须先出单独计划，不允许顺手改变语义。
