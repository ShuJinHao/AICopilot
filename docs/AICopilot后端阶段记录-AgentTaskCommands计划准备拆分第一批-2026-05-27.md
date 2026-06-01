# AICopilot 后端阶段记录 - AgentTaskCommands 计划准备拆分第一批 - 2026-05-27

## 本批目标

- 只改 `AICopilot` 后端。
- 继续拆分 `AgentTaskCommands.cs`，把计划准备、权限校验、静态步骤构建和计划元数据 helper 从主 handler 中剥离。
- 保持 `PlanAgentTaskCommandHandler` 类型名、构造参数、public `Handle` 签名和运行行为不变。
- 不新增功能，不改 MCP 部署，不触碰 Cloud/Edge/UI/数据库迁移/部署编排。

## 实际改动

- `AgentTaskCommands.cs` 收敛为 plan command 主编排：保留 command records、planner model 解析、ToolRegistry guard 调用、plan 持久化、workspace/approval/audit 写入。
- 新增 `AgentTaskPlanPreparationService.cs`，承接 session/upload 校验、RAG knowledge base 读权限复核、CloudReadonly trial 互斥校验、BusinessDatabase data source 校验、business domain 推导和 plan flag 计算。
- 新增 `AgentTaskPlanPreparation.cs`，承接准备阶段结果，减少 handler 中的局部变量堆积。
- 新增 `AgentTaskPlanStepBuilder.cs`，承接 `BuildPlanSteps`、`EnsureMandatorySteps`、artifact type normalize 和 artifact step insertion。
- 新增 `AgentTaskPlanMetadataBuilder.cs`，承接 planner mode、risk level、title、data source summary 和 tool risk summary 这类纯 helper。
- 更新 `SecurityHardeningTests` 的 RAG 权限源码扫描路径，从 `AgentTaskCommands.cs` 迁移到 `AgentTaskPlanPreparationService.cs`，断言内容未放松。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.AiGatewayService/AgentTasks`
- 能力边界：Agent task planning 内部结构拆分。
- 公开契约：未修改 command/query/DTO 字段、permission attributes、status 字符串、handler 构造参数和 public 方法签名。
- 运行行为：未修改 RAG `CanReadAsync` 复核、`Result.NotFound` 隐藏策略、CloudReadonly sandbox/production pilot 互斥规则、BusinessDatabase simulation-only 限制、ToolRegistry data boundary、approval checkpoint 或 artifact step 语义。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改数据库迁移、部署编排、MCP 配置、公开 DTO/API 语义。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~ToolRegistryGovernanceTests|FullyQualifiedName~EnterpriseDynamicPlannerP3Tests|FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~EnterpriseCloudReadonlySandboxAgentTrialP7Tests|FullyQualifiedName~EnterpriseCloudReadonlySandboxExpansionP8Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionPilotP12Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionControlledPilotP13Tests"
```

- 结果：通过 155 / 155，失败 0，跳过 0。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 44 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0。

```bash
git diff --check
```

- 结果：通过，无 whitespace error。

```bash
wc -l src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskCommands.cs src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskPlan*.cs
```

- `AgentTaskCommands.cs`：477 行。
- `AgentTaskPlanDocument.cs`：96 行。
- `AgentTaskPlanMetadataBuilder.cs`：86 行。
- `AgentTaskPlanPreparation.cs`：18 行。
- `AgentTaskPlanPreparationService.cs`：245 行。
- `AgentTaskPlanStepBuilder.cs`：368 行。

```bash
git diff --name-only && git ls-files --others --exclude-standard
```

- 结果：已执行；当前 dirty worktree 中存在前序批次的 AICopilot 改动，本批新增/修改范围为 `AgentTaskCommands.cs`、`AgentTaskPlanPreparation.cs`、`AgentTaskPlanPreparationService.cs`、`AgentTaskPlanStepBuilder.cs`、`AgentTaskPlanMetadataBuilder.cs`、`SecurityHardeningTests.cs` 和本阶段记录文档。

## 剩余风险

- 本批只拆 `AgentTaskCommands.cs` 的计划准备和静态步骤构建，没有处理 `CloudReadonlySimulation.cs`、`AgentReportComposer.cs`、`AgentApprovalManagement.cs` 等后续结构债。
- `AgentTaskPlanStepBuilder.cs` 当前 368 行，低于 500 行，但后续若继续新增 artifact step 类型，应优先拆出 artifact step policy，避免重新膨胀。
- `ArtifactVersioningManagement.cs` 仍是当前更大的 service 文件之一，但属于 workspace 版本治理，应单独规划，不与 agent planning 混在一批。

## 下一阶段进入条件

- 从当前绿色基线继续：`BackendTests 759/759`、`ArchitectureTests 44/44`。
- 下一批建议在 `AgentTasks` 内继续处理 `CloudReadonlySimulation.cs` 或 `AgentReportComposer.cs`；如果转向 workspace，则先单独规划 `ArtifactVersioningManagement.cs`。
- 如果下一批涉及权限源码扫描断言，只允许迁移扫描路径，不允许放松权限或安全断言。
