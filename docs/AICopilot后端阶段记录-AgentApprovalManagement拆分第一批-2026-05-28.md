# AICopilot 后端阶段记录 - AgentApprovalManagement 拆分第一批 - 2026-05-28

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `AgentApprovalManagement.cs`，把审批 query handlers、decision handlers、decision workflow、access 和 DTO mapping 从单体文件中剥离。
- 保持审批权限、状态流转、审计、tool approval resume queue 和返回 DTO 语义不变。
- 不新增审批类型，不改 MCP 部署，不触碰 Cloud/Edge/UI/数据库迁移/部署编排。

## 实际改动

- `AgentApprovalManagement.cs` 收敛为审批 contracts/入口声明文件，仅保留 DTO、decision request、queries 和 commands。
- 新增 `AgentApprovalQueryHandlers.cs`，承接 pending approvals 和 task approvals 查询 handler。
- 新增 `AgentApprovalDecisionCommandHandlers.cs`，承接 approve/reject command handlers，保持 public handler 类型名、构造参数和 `Handle` 签名不变。
- 新增 `AgentApprovalDecisionWorkflow.cs`，承接 shared decision flow、decision task loading、approval/rejection 状态推进和 decision summary。
- 新增 `AgentApprovalAccess.cs`，承接 missing user、task loading 和 workspace loading。
- 新增 `AgentApprovalDtoMapper.cs`，承接 approval DTO mapping、target name、risk level、reason 和 step lookup。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.AiGatewayService/AgentTasks`
- 能力边界：Human-in-the-loop 审批管理内部结构拆分。
- 公开契约：未修改 command/query/DTO 字段、permission attributes、status 字符串、repository specs、数据库实体、数据库迁移或 DI 注册语义。
- 运行行为：未修改 self-user/cross-user approval 可见性、`AgentApprovalPermissions` 判定、Plan/ToolCall/Artifact/FinalOutput 状态推进、reject 失败语义、tool approval 后 `ApprovalResume` 入队、`RecordApprovalDecisionAsync` 和 `SaveChangesAsync` 边界。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 contracts/DTO 语义、数据库迁移、部署编排或 MCP 配置。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~AgentApprovalPermissionHardeningTests|FullyQualifiedName~ToolRegistryGovernanceTests|FullyQualifiedName~AcceptanceClosureVerificationTests|FullyQualifiedName~AgentSimulationAcceptanceTests|FullyQualifiedName~EnterpriseArtifactWorkspaceP9Tests|FullyQualifiedName~IdentityAccessManagementTests|FullyQualifiedName~FreshDatabaseSeedTests"
```

- 结果：通过 66 / 66，失败 0，跳过 0，耗时 2 m 22 s。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 36 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0。

```bash
wc -l src/services/AICopilot.AiGatewayService/AgentTasks/AgentApproval*.cs
```

- `AgentApprovalManagement.cs`：31 行。
- `AgentApprovalAccess.cs`：57 行。
- `AgentApprovalDecisionCommandHandlers.cs`：94 行。
- `AgentApprovalDecisionWorkflow.cs`：233 行。
- `AgentApprovalDtoMapper.cs`：100 行。
- `AgentApprovalPermissions.cs`：81 行。
- `AgentApprovalQueryHandlers.cs`：107 行。

## 剩余风险

- 本批只处理 `AgentApprovalManagement.cs`，没有处理 `AgentDynamicPlanner.cs`。
- `AgentApprovalDecisionWorkflow.cs` 当前 233 行，低于 500 行；后续如果新增审批类型，应优先扩展 workflow 内的审批状态处理，并同步补充窄测。
- 本批保留 `ApproveAgentApprovalCommandHandler.DecideAsync` 作为内部转发入口，降低已有内部调用路径的迁移风险。

## 下一阶段进入条件

- 从当前绿色基线继续：`BackendTests 759/759`、`ArchitectureTests 44/44`。
- 下一批建议处理 `AgentDynamicPlanner.cs`，它是 `AgentTasks` 中最后一个超过 500 行的文件。
- 如果下一批涉及动态规划 prompt、ToolRegistry data boundary、RAG 权限复核或 planner contract 解析，只允许结构迁移，不允许改变计划生成语义。
