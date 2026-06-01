# AICopilot 后端阶段记录 - PilotAuthorizationWorkflow 拆分第一批 - 2026-05-27

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `PilotAuthorizationWorkflow.cs`，降低 Pilot authorization 单文件职责密度。
- 保持 M2 授权流程、状态流转、权限、审计、GateState、敏感信息脱敏和测试行为不变。
- 不新增功能，不改 MCP 部署，不触碰 Cloud/Edge/UI/数据库迁移/部署编排。

## 实际改动

- `PilotAuthorizationWorkflow.cs` 收敛为 contracts 文件，仅保留 permissions、audit action 常量、DTO、request、commands 和 queries。
- 新增 `PilotAuthorizationQueryHandlers.cs`，承接 list/detail/audit timeline query handlers。
- 新增 `PilotAuthorizationSubmissionCommandHandlers.cs`，承接 create/update/submit handlers，并保留 unsafe draft explicit audit save 语义。
- 新增 `PilotAuthorizationDecisionCommandHandlers.cs`，承接 approve/reject/revoke/expire handlers 与 shared decision helper，并保留 unsafe decision/self-review explicit audit save 语义。
- 新增 `PilotAuthorizationMachineValidator.cs`，承接机器校验规则。
- 新增 `PilotAuthorizationAccess.cs`，承接当前用户访问控制和 submission loading。
- 新增 `PilotAuthorizationExpiryWorker.cs`，承接过期 worker 和 system expiry。
- 新增 `PilotAuthorizationProjection.cs`，承接 mapper、GateState 和 safe text redaction。
- 新增 `PilotAuthorizationAudit.cs`，承接 audit write、audit timeline mapping 和 metadata sanitize。
- 更新源码扫描测试读取路径与 explicit audit save 白名单；断言内容未放松。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.AiGatewayService/PilotAuthorization`
- 能力边界：Pilot authorization 内部结构拆分。
- 公开契约：未修改 DTO 字段、commands、queries、permission attributes、status 字符串、audit action 字符串、handler/validator/worker 构造参数和 public/static 方法签名。
- 运行行为：未修改 self-review forbidden、machine rejection、credential-window planning、limited-pilot planning、reject/revoke/expire、system expiry、GateState、脱敏和 audit timeline 返回语义。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改数据库迁移、部署编排、MCP 配置、公开 DTO/API 语义。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~PilotAuthorizationWorkflowM2Tests|FullyQualifiedName~AICopilotM2_2ReadinessScopeTests|FullyQualifiedName~AICopilotM2M9GovernanceScopeTests|FullyQualifiedName~SecurityHardeningTests"
```

- 结果：通过 95 / 95，失败 0，跳过 0。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 42 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0。

```bash
wc -l src/services/AICopilot.AiGatewayService/PilotAuthorization/*PilotAuthorization*.cs
```

- `PilotAuthorizationAccess.cs`：114 行。
- `PilotAuthorizationAudit.cs`：120 行。
- `PilotAuthorizationDecisionCommandHandlers.cs`：266 行。
- `PilotAuthorizationExpiryWorker.cs`：85 行。
- `PilotAuthorizationMachineValidator.cs`：138 行。
- `PilotAuthorizationProjection.cs`：121 行。
- `PilotAuthorizationQueryHandlers.cs`：119 行。
- `PilotAuthorizationSubmissionCommandHandlers.cs`：243 行。
- `PilotAuthorizationWorkflow.cs`：210 行。

## 剩余风险

- 本批只做 Pilot authorization 结构拆分，没有处理 MCP 独立容器、部署配置修复或 AgentTaskCommands 进一步拆分。
- `AgentTaskCommands.cs` 仍为后续更大的结构债候选，但其涉及 planner、CloudReadonly trial、BusinessDatabase、approval 多条链路，应单独规划。

## 下一阶段进入条件

- 从当前绿色基线继续：`BackendTests 759/759`、`ArchitectureTests 44/44`。
- 下一批建议先评估 `AgentTaskCommands.cs`，优先选择生命周期 handler/loader 或静态 planner helper 这类低风险边界拆分。
- 若进入 MCP 部署修复，必须单独规划 MCP server 包、SSE endpoint、compose profile 和非破坏性配置迁移步骤。
