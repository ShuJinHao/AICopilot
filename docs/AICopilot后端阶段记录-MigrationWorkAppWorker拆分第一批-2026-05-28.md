# AICopilot 后端阶段记录 - MigrationWorkApp Worker 拆分第一批 - 2026-05-28

## 本批目标

- 只修改 `AICopilot` 后端 Host 层。
- 拆分 `AICopilot.MigrationWorkApp/Worker.cs`，将迁移执行、Identity seed、AiGateway 默认 seed、Cloud semantic simulation source bootstrap 从主 Worker 中剥离。
- 保持迁移顺序、seed 值、角色/权限同步、ToolRegistry 默认 seed、CloudReadonly protected tool 默认关闭、SQL 文本、save 边界和错误语义不变。
- 不修改数据库迁移、部署编排、MCP 配置、公开 API/DTO 或跨项目代码。

## 实际改动类别

- `Worker.cs` 收敛为 Host 编排层：创建 scope、解析依赖、按原顺序调用 helper、停止应用。
- 新增 `MigrationWorkerDatabaseMigrator.cs`，承接 migration context 构建、legacy history bootstrap 和 DbContext migration 执行。
- 新增 `MigrationWorkerIdentitySeeder.cs`，承接 Admin/User 角色、权限同步和 BootstrapAdmin seed。
- 新增 `MigrationWorkerAiGatewaySeeder.cs`，承接 Chat runtime settings、disabled example model、conversation template、artifact approval policy、tool registration 和 routing model seed。
- 新增 `MigrationWorkerCloudSimulationSeeder.cs`，承接 `cloud-device-semantic-sim` 连接串检查与 Cloud simulation SQL 初始化。
- 更新源码扫描测试读取新迁移 helper 文件，不放松原迁移顺序和 split history table 断言。

## 影响模块

- `AICopilot/src/hosts/AICopilot.MigrationWorkApp/`
- `AICopilot/src/tests/AICopilot.BackendTests/MigrationOwnershipTests.cs`
- `AICopilot/src/tests/AICopilot.ArchitectureTests/ArchitectureBoundaryTests.cs`
- 影响能力：MigrationWorkApp 启动迁移与 seed 的文件组织。
- 不影响能力：Identity 权限语义、AiGateway 默认数据语义、CloudReadonly 只读边界、数据库 schema、部署与 MCP。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改 Core 实体、Services 行为、Infrastructure EF mapping、数据库迁移、容器编排或 MCP 配置。
- 未新增 NuGet/npm/容器依赖。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

- `dotnet build src/hosts/AICopilot.MigrationWorkApp/AICopilot.MigrationWorkApp.csproj --no-restore`
  - 结果：通过，0 warning / 0 error。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~MigrationSafetyTests|FullyQualifiedName~FreshDatabaseSeedTests|FullyQualifiedName~IdentityAccessManagementTests|FullyQualifiedName~AICopilotM6SecurityComplianceHardeningTests|FullyQualifiedName~SecurityHardeningTests"`
  - 结果：通过 78/78。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
  - 结果：通过 759/759。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
  - 结果：通过 44/44。
- `git diff --check`
  - 结果：通过，无 whitespace error 输出。
- `git diff --name-only`
  - 结果：当前工作树包含前序多批 AICopilot 拆分改动；本批 tracked 改动包含 `Worker.cs`、`ArchitectureBoundaryTests.cs`、`MigrationOwnershipTests.cs`。
- `wc -l src/hosts/AICopilot.MigrationWorkApp/Worker.cs src/hosts/AICopilot.MigrationWorkApp/MigrationWorker*.cs`
  - 结果：`Worker.cs` 62 行，`MigrationWorkerAiGatewaySeeder.cs` 230 行，`MigrationWorkerCloudSimulationSeeder.cs` 234 行，`MigrationWorkerDatabaseMigrator.cs` 44 行，`MigrationWorkerIdentitySeeder.cs` 73 行，均低于 500 行。

## 剩余风险

- 本批是 Host 层结构拆分，未改变迁移或 seed 行为。
- `MigrationWorkApp` 的真实数据库运行路径仍依赖现有测试 AppHost/fixture 覆盖；本批不新增数据库迁移或真实部署演练。
- 当前工作树包含前序多批 AICopilot 拆分改动，本批仅在其基础上追加 MigrationWorkApp Worker 拆分。

## 下一阶段进入条件

- `AICopilot.BackendTests` 与 `ArchitectureTests` 保持绿色。
- 如后续继续收口结构债，可再评估 `CloudReadonlyContracts.cs`、`DependencyInjection.cs`、`PilotAuthorizationSubmission.cs` 等剩余 500 行以上文件。
- 如果后续需要改变 SQL、迁移顺序、seed 值、角色/权限策略、ToolRegistry seed、配置 key、数据库结构、DI 注册或部署配置，必须单独开批并重新确认范围。
