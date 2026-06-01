# AICopilot 后端阶段记录 - ModelProviderReliability 拆分第一批 - 2026-05-28

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `ModelProviderReliability.cs`，把 provider reliability policies、snapshot reader、endpoint pool scheduler 和 runtime state 从单体文件中剥离。
- 保持 provider reliability options、model endpoint options、public records、interfaces、DI 注册语义和运行行为不变。
- 不新增 provider reliability 功能，不改模型调用行为，不改配置、权限、前端、数据库、部署编排或 MCP 配置。

## 实际改动

- `ModelProviderReliability.cs` 收敛为 contracts/入口文件，仅保留 options、records、interfaces 和 `ModelEndpointPoolNotConfiguredException`。
- 新增 `ModelProviderReliabilityPolicies.cs`，承接 always-healthy provider health、fallback policy、in-memory circuit breaker、cost budget policy 和 fallback scope 常量。
- 新增 `ModelProviderReliabilitySnapshotReader.cs`，承接 provider reliability snapshot 构建。
- 新增 `ModelEndpointPoolScheduler.cs`，承接 endpoint acquire/select/lease release、queue、quota、pool selection、credential resolution 和 endpoint redaction。
- 新增 `ModelEndpointRuntimeState.cs`，承接 endpoint stats、model stats、quota window 和 token window runtime state。
- 更新 `AICopilotM6SecurityComplianceHardeningTests` 源码扫描路径：`HasApiKey` 继续从 contracts 文件检查，`RedactedEndpointMarker` 和 `[redacted-endpoint]` 从 scheduler 文件检查。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/infrastructure/AICopilot.AiRuntime`
- 能力边界：model provider fallback、circuit breaker、cost budget、endpoint pool scheduling、runtime snapshot 内部结构拆分。
- 公开契约：未修改 `ModelProviderReliabilityOptions`、`ModelEndpointPoolOptions`、`ModelEndpointOptions`、`ModelEndpointSelection`、`ModelProviderExecutionContext`、scheduler/policy interfaces 或 contracts DTO。
- 运行行为：未修改 fallback 禁用高风险链路、circuit breaker、rate limit/queue、lease dispose、secret/endpoint redaction、snapshot DTO wire shape 或 DI 注册语义。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 DTO/API 语义、数据库迁移、部署编排或 MCP 配置。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~ModelProviderReliabilityTests|FullyQualifiedName~AICopilotM3_1ModelPoolRuntimeTests|FullyQualifiedName~EnterpriseDataGovernanceP0Tests|FullyQualifiedName~EnterpriseDataGovernanceP1Tests|FullyQualifiedName~AICopilotM6SecurityComplianceHardeningTests|FullyQualifiedName~SecurityHardeningTests"
```

- 结果：通过 106 / 106，失败 0，跳过 0，耗时 212 ms。
- 备注：首次运行发现 M6 源码扫描仍从旧单体文件读取 `HasApiKey` 与 redaction marker；已按实际文件职责拆成 contracts/scheduler 两处读取后通过。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 37 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0，耗时 527 ms。

```bash
wc -l src/infrastructure/AICopilot.AiRuntime/*Model*Reliability*.cs src/infrastructure/AICopilot.AiRuntime/ModelEndpoint*.cs
```

- `ModelProviderReliability.cs`：189 行。
- `ModelProviderReliabilityPolicies.cs`：140 行。
- `ModelProviderReliabilitySnapshotReader.cs`：41 行。
- `ModelEndpointPoolScheduler.cs`：493 行。
- `ModelEndpointRuntimeState.cs`：196 行。

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

- 本批只处理 AiRuntime provider reliability 单体，没有拆 `AiGatewayController.cs`、core domain aggregate 或 artifact document infrastructure。
- `ModelEndpointPoolScheduler.cs` 仍接近 500 行，但职责已经收敛为 endpoint pool 主编排；下一次新增调度策略前应优先评估继续拆 queue/quota/selection helper。
- 本批只迁移 M6 源码扫描路径，未放松安全断言。

## 下一阶段进入条件

- 需要保持当前绿色基线：`BackendTests` 全量通过、`ArchitectureTests` 全量通过。
- 下一批建议在单独计划中评估 `AiGatewayController.cs`、`ProductionPilotOperations.cs`、`AgentArtifactDocumentServices.cs` 或 `ToolRegistryManagement.cs`。
- 如果下一批涉及 fallback 策略、high-risk tool chain 禁用规则、circuit breaker、rate limit/queue 语义、secret/endpoint redaction、公开接口、配置结构、数据库或部署配置，必须先出单独计划，不允许顺手改变语义。
