# AICopilot 后端阶段记录 - ArtifactVersioningManagement 拆分第一批 - 2026-05-28

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `ArtifactVersioningManagement.cs`，把 artifact versioning handlers、access、policy、file versioning 和 text diff 从单体文件中剥离。
- 保持 artifact versioning DTO、request、query、command records、public handler 构造参数和 `Handle` 签名不变。
- 不新增 artifact versioning 功能，不改前端契约，不改文件存储接口，不改数据库、部署编排或 MCP 配置。

## 实际改动

- `ArtifactVersioningManagement.cs` 收敛为 contracts 文件，仅保留 DTO、request、query 和 command records。
- 新增 `ArtifactVersioningQueryHandlers.cs`，承接 content、versions、download、diff query handlers。
- 新增 `ArtifactVersioningCommandHandlers.cs`，承接 update content、restore version command handlers。
- 新增 `ArtifactVersioningAccess.cs`，承接 `ArtifactVersioningContext`、read/edit access loading。
- 新增 `ArtifactVersioningPolicy.cs`，承接 text artifact 校验、edit window、CanEdit 和 content/diff limits。
- 新增 `ArtifactVersioningFiles.cs`，承接 version list/open/read/archive、metadata、sha256 和 version path。
- 新增 `ArtifactTextDiffer.cs`，承接 line diff 和 modified entry coalesce。
- 更新 `SecurityHardeningTests` explicit audit save 白名单，从旧单体文件迁移到实际包含 `auditLogWriter.SaveChangesAsync` 的 query/command handler 文件。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.AiGatewayService/Workspaces`
- 能力边界：Artifact workspace draft text content、version archive、restore、download 和 diff 内部结构拆分。
- 公开契约：未修改 DTO 字段、requests、commands、queries、permission attributes、status 字符串、repository specs、数据库实体、数据库迁移或 DI 注册语义。
- 运行行为：未修改 content read/edit、version archive、restore、download audit、diff output、editable 判定、final review lock、expectedVersion 冲突、sha256 或 metadata JSON 语义。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改数据库迁移、部署编排或 MCP 配置。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~AgentArtifactVersioningTests|FullyQualifiedName~EnterpriseArtifactWorkspaceP9Tests|FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~FrontendIntegrationContractTests|FullyQualifiedName~AcceptanceClosureVerificationTests"
```

- 结果：通过 70 / 70，失败 0，跳过 0，耗时 29 s。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 38 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0，耗时 328 ms。

```bash
wc -l src/services/AICopilot.AiGatewayService/Workspaces/*ArtifactVersioning*.cs src/services/AICopilot.AiGatewayService/Workspaces/ArtifactTextDiffer.cs
```

- `ArtifactVersioningManagement.cs`：69 行。
- `ArtifactVersioningQueryHandlers.cs`：250 行。
- `ArtifactVersioningCommandHandlers.cs`：200 行。
- `ArtifactVersioningAccess.cs`：144 行。
- `ArtifactVersioningPolicy.cs`：93 行。
- `ArtifactVersioningFiles.cs`：313 行。
- `ArtifactTextDiffer.cs`：141 行。

```bash
git diff --check
```

- 结果：通过，无 whitespace 错误输出。

## 剩余风险

- 本批只处理 `ArtifactVersioningManagement.cs`，没有拆 `ArtifactWorkspaceManagement.cs`、`ArtifactWorkspaceP9Management.cs` 或 controller 层。
- `ArtifactVersioningFiles.cs` 仍包含文件读取、metadata 和 sha256 helper，但已低于 500 行；后续如果新增版本文件能力，应优先在该文件内扩展并补测试。
- `SecurityHardeningTests.cs` 本轮只迁移 explicit audit save 白名单路径，未放松断言。

## 下一阶段进入条件

- 需要保持当前绿色基线：`BackendTests` 全量通过、`ArchitectureTests` 全量通过。
- 下一批建议继续处理 `Workspaces` 中的 `ArtifactWorkspaceManagement.cs` 或转向 `AiGatewayController.cs`，但必须单独规划文件白名单。
- 如果下一批涉及 artifact version API、权限策略、edit lock、audit save 边界、metadata 格式、diff 语义、公开接口、数据库结构或部署配置，必须先出单独计划，不允许顺手改变语义。
