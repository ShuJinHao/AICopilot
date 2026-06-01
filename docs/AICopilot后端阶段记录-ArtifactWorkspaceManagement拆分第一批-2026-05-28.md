# AICopilot 后端阶段记录 - ArtifactWorkspaceManagement 拆分第一批 - 2026-05-28

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `ArtifactWorkspaceManagement.cs`，把 artifact workspace service、mapper、query handlers、command handlers 和 access helper 从单体文件中剥离。
- 保持 artifact workspace DTO、query、command、`IAgentArtifactWorkspaceService`、public handler 构造参数和 `Handle` 签名不变。
- 不新增 workspace 功能，不改前端契约，不改文件存储接口，不改数据库、部署编排或 MCP 配置。

## 实际改动

- `ArtifactWorkspaceManagement.cs` 收敛为 contracts 文件，仅保留 DTO、query、command 和 `IAgentArtifactWorkspaceService` 声明。
- 新增 `ArtifactWorkspaceService.cs`，承接 `AgentArtifactWorkspaceService`、workspace 创建、draft text/binary artifact 写入和 finalized workspace 写入拦截。
- 新增 `ArtifactWorkspaceMapper.cs`，承接 artifact、manifest、draft/final artifact、preview/status 和 source metadata DTO 映射。
- 新增 `ArtifactWorkspaceQueryHandlers.cs`，承接 settings、workspace 和 artifact download query handlers，并保留 download audit write/save。
- 新增 `ArtifactWorkspaceCommandHandlers.cs`，承接 submit final review 和 finalize command handlers，并保留 explicit audit write/save。
- 新增 `ArtifactWorkspaceAccess.cs`，承接 `WorkspaceAccess`、owner/permission workspace loading 和 final output approval gate。
- 更新 `SecurityHardeningTests` explicit audit save 白名单，从旧单体文件迁移到实际包含 `auditLogWriter.SaveChangesAsync` 的 query/command handler 文件。
- 更新 `AICopilotM6SecurityComplianceHardeningTests` 的 artifact download 源码扫描路径，从旧单体文件迁移到新的 query handler 文件，断言内容未放松。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.AiGatewayService/Workspaces`
- 能力边界：Artifact workspace 创建、draft 写入、workspace 查询、artifact download、final review submit 和 finalize 内部结构拆分。
- 公开契约：未修改 DTO 字段、queries、commands、permission attributes、status 字符串、repository specs、数据库实体、数据库迁移或 DI 注册语义。
- 运行行为：未修改 workspace 创建、draft 写入、download audit、final review submit、finalize、owner/privileged access、final-output approval gate、manifest/source metadata 映射语义。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改数据库迁移、部署编排或 MCP 配置。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~AgentArtifactVersioningTests|FullyQualifiedName~EnterpriseArtifactWorkspaceP9Tests|FullyQualifiedName~EnterpriseAgentWorkbenchP2Tests|FullyQualifiedName~AgentArtifactGenerationTests|FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~FrontendIntegrationContractTests|FullyQualifiedName~AcceptanceClosureVerificationTests"
```

- 结果：通过 78 / 78，失败 0，跳过 0，耗时 41 s。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~AICopilotM6SecurityComplianceHardeningTests.ArtifactDownload_ShouldRequirePermissionAndWriteDownloadAudit"
```

- 结果：通过 1 / 1，失败 0，跳过 0，耗时 9 ms。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 45 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0，耗时 522 ms。

```bash
wc -l src/services/AICopilot.AiGatewayService/Workspaces/*ArtifactWorkspace*.cs
```

- `ArtifactWorkspaceManagement.cs`：122 行。
- `ArtifactWorkspaceService.cs`：113 行。
- `ArtifactWorkspaceMapper.cs`：136 行。
- `ArtifactWorkspaceQueryHandlers.cs`：148 行。
- `ArtifactWorkspaceCommandHandlers.cs`：265 行。
- `ArtifactWorkspaceAccess.cs`：132 行。
- `ArtifactWorkspaceP9Management.cs`：755 行，既有下一批结构债，不属于本批拆分目标。

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

- 本批只处理 `ArtifactWorkspaceManagement.cs`，没有拆 `ArtifactWorkspaceP9Management.cs`、`TrialOperationsManagement.cs` 或 controller 层。
- `ArtifactWorkspaceP9Management.cs` 当前仍为 755 行，保留为下一批 Workspaces 结构债。
- `ArtifactWorkspaceCommandHandlers.cs` 保留 final review/finalize 状态推进和 audit save 边界；后续如果修改 finalization 语义，需要单独规划。
- `SecurityHardeningTests.cs` 本轮只迁移 explicit audit save 白名单路径，未放松断言。

## 下一阶段进入条件

- 需要保持当前绿色基线：`BackendTests` 全量通过、`ArchitectureTests` 全量通过。
- 下一批建议继续处理 `Workspaces` 中的 `ArtifactWorkspaceP9Management.cs`，或转向更大的 `TrialOperationsManagement.cs`，但必须单独规划文件白名单。
- 如果下一批涉及 artifact workspace API、权限策略、final review/finalize 语义、audit save 边界、mapper wire shape、文件存储契约、公开接口、数据库结构或部署配置，必须先出单独计划，不允许顺手改变语义。
