# AICopilot 后端阶段记录 - PilotAuthorizationSubmission 拆分第一批 - 2026-06-01

## 本批目标

- 只修改 `AICopilot` 后端 Core 层。
- 拆分 `PilotAuthorizationSubmission.cs` 中堆叠的 status enum、machine validation result、review timeline 和 material value records。
- 保持 `PilotAuthorizationSubmission` 聚合根状态机、构造参数、公开属性、normalize helper、异常文案和敏感文本校验行为不变。
- 不修改 Services 行为、Infrastructure EF 配置、数据库迁移、部署编排、MCP 配置或公开 API/DTO 语义。

## 实际改动类别

- `PilotAuthorizationSubmission.cs` 保留聚合根主逻辑：draft/update/submit/approve/reject/revoke/expire、system expiry、状态校验和 normalize helper。
- 新增 `PilotAuthorizationSubmissionStatus.cs`，承接 `PilotAuthorizationSubmissionStatus` enum。
- 新增 `PilotAuthorizationMachineValidationResult.cs`，承接 machine validation result。
- 新增 `PilotAuthorizationReview.cs`，承接 review timeline 与 decision marker。
- 新增 `PilotAuthorizationMaterials.cs`，承接 `PilotCredentialWindow`、`PilotRollbackPlan`、`PilotEvidenceArchive`、`PilotAuthorizationMaterialIntake`。

## 影响模块

- `AICopilot/src/core/AICopilot.Core.AiGateway/Aggregates/PilotAuthorization/`
- 影响能力：Core 聚合文件组织。
- 不影响能力：M2 授权流程、状态流转、机器拒绝、review pending、credential-window planning、limited-pilot planning、reject/revoke/expire、system expiry、敏感文本拦截、audit 投影和 EF owned type 映射。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改 Services、Infrastructure、Host、EF mapping、数据库迁移、部署编排或 MCP 配置。
- 未新增 NuGet/npm/容器依赖。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

- `dotnet build src/core/AICopilot.Core.AiGateway/AICopilot.Core.AiGateway.csproj --no-restore`
  - 结果：通过，0 warning / 0 error。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~PilotAuthorizationWorkflowM2Tests|FullyQualifiedName~AICopilotM2M9GovernanceScopeTests|FullyQualifiedName~AICopilotM2_2ReadinessScopeTests|FullyQualifiedName~SecurityHardeningTests"`
  - 结果：通过，95 passed / 0 failed。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
  - 结果：通过，759 passed / 0 failed。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
  - 结果：通过，44 passed / 0 failed。
- `git diff --check`
  - 结果：通过，无 whitespace error。
- `git diff --name-only`
  - 结果：仅列出当前 AICopilot 工作树内 tracked diff；工作树仍包含前序多批 AICopilot 拆分改动。本批新增 PilotAuthorization Core 拆分文件和本阶段记录为 untracked 文件，需结合 `git status --short --untracked-files=all` 审查。
- `wc -l src/core/AICopilot.Core.AiGateway/Aggregates/PilotAuthorization/*PilotAuthorization*.cs`
  - 结果：`PilotAuthorizationSubmission.cs` 432 行，`PilotAuthorizationSensitiveContentGuard.cs` 108 行，`PilotAuthorizationMaterials.cs` 44 行，`PilotAuthorizationReview.cs` 42 行，`PilotAuthorizationSubmissionStatus.cs` 14 行，`PilotAuthorizationMachineValidationResult.cs` 11 行，单文件均低于 500 行。

## 剩余风险

- 本批是 Core 文件组织拆分，未改变聚合行为。
- 当前工作树包含前序多批 AICopilot 拆分改动，本批只追加 PilotAuthorizationSubmission 聚合拆分。
- 未做真实部署演练；运行行为由 BackendTests 全量和 ArchitectureTests 覆盖。

## 下一阶段进入条件

- `AICopilot.BackendTests` 与 `AICopilot.ArchitectureTests` 已保持绿色，可进入下一批。
- 后续可继续评估 `SemanticSummaryProfiles.cs` 或 `McpServerBootstrap.cs` 等剩余 500 行附近文件。
- 如果后续需要改变实体字段、构造语义、状态流转、异常文案、敏感内容校验、EF mapping、repository 行为、公开接口、数据库结构或部署配置，必须单独开批并重新确认范围。
