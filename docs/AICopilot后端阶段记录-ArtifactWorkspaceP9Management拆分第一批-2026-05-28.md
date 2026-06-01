# AICopilot 后端阶段记录 - ArtifactWorkspaceP9Management 拆分第一批 - 2026-05-28

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `ArtifactWorkspaceP9Management.cs`，把 P9 preview、revision comment、regenerate draft、submit artifact final approval、policy 和 preview builder 从单体文件中剥离。
- 保持 P9 DTO、request、query、command records、public handler 构造参数和 `Handle` 签名不变。
- 不新增 P9 功能，不改前端契约，不改文件存储接口，不改数据库、部署编排或 MCP 配置。

## 实际改动

- `ArtifactWorkspaceP9Management.cs` 收敛为 contracts 文件，仅保留 preview DTO、revision comment DTO、requests、queries 和 commands。
- 新增 `ArtifactWorkspaceP9QueryHandlers.cs`，承接 `GetAgentArtifactPreviewQueryHandler`，并保留 preview audit write/save。
- 新增 `ArtifactWorkspaceP9CommandHandlers.cs`，承接 revision comment、regenerate draft 和 submit artifact final approval command handlers，并保留 explicit audit write/save。
- 新增 `ArtifactWorkspaceP9Policy.cs`，承接 draft mutation 校验、final review lock、expectedVersion 校验和 comment hash。
- 新增 `ArtifactPreviewBuilder.cs`，承接 markdown/html/json/chart/csv/pdf/pptx/xlsx preview 构建、CSV/XLSX 解析、metadata/status helper。
- 更新 `SecurityHardeningTests` explicit audit save 白名单，从旧单体文件迁移到实际包含 `auditLogWriter.SaveChangesAsync` 的 P9 query/command handler 文件。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.AiGatewayService/Workspaces`
- 能力边界：Artifact P9 preview、revision comment、draft regenerate、single artifact final review submission 内部结构拆分。
- 公开契约：未修改 DTO 字段、requests、queries、commands、permission attributes、status 字符串、repository specs、数据库实体、数据库迁移或 DI 注册语义。
- 运行行为：未修改 preview kind、preview metadata、CSV/XLSX 行列投影、binary preview 限制、draft mutation lock、final review submission、audit payload 或 workspace DTO 映射语义。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改数据库迁移、部署编排或 MCP 配置。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~EnterpriseArtifactWorkspaceP9Tests|FullyQualifiedName~AgentArtifactVersioningTests|FullyQualifiedName~AgentApprovalPermissionHardeningTests|FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~FrontendIntegrationContractTests|FullyQualifiedName~AcceptanceClosureVerificationTests"
```

- 结果：通过 73 / 73，失败 0，跳过 0，耗时 59 s。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 38 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0，耗时 360 ms。

```bash
wc -l src/services/AICopilot.AiGatewayService/Workspaces/*ArtifactWorkspaceP9*.cs src/services/AICopilot.AiGatewayService/Workspaces/ArtifactPreviewBuilder.cs
```

- `ArtifactWorkspaceP9Management.cs`：53 行。
- `ArtifactWorkspaceP9QueryHandlers.cs`：67 行。
- `ArtifactWorkspaceP9CommandHandlers.cs`：257 行。
- `ArtifactWorkspaceP9Policy.cs`：58 行。
- `ArtifactPreviewBuilder.cs`：347 行。

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

- 本批只处理 `ArtifactWorkspaceP9Management.cs`，没有拆 `TrialOperationsManagement.cs`、controller 层或 infrastructure artifact document service。
- `ArtifactPreviewBuilder.cs` 仍集中承载多格式 preview 解析，但拆分后独立成专用 helper；后续如果新增 preview 格式，应优先评估是否继续按格式拆分。
- `SecurityHardeningTests.cs` 本轮只迁移 explicit audit save 白名单路径，未放松断言。

## 下一阶段进入条件

- 需要保持当前绿色基线：`BackendTests` 全量通过、`ArchitectureTests` 全量通过。
- 下一批建议转向 Workspaces 外的 `TrialOperationsManagement.cs` 或 `AiGatewayController.cs`，但必须单独规划文件白名单。
- 如果下一批涉及 P9 API、权限策略、final review lock、audit save 边界、preview wire shape、文件存储契约、公开接口、数据库结构或部署配置，必须先出单独计划，不允许顺手改变语义。
