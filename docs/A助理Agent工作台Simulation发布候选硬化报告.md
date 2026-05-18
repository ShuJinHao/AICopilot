# A助理 Agent 工作台 Simulation 发布候选硬化报告

日期：2026-05-18

## 已硬化

- 新增 `scripts/Test-AgentWorkbenchReleaseCandidateScope.ps1`，用于 Simulation 发布候选阶段检查跨项目边界、默认 CloudReadonly/CloudAiRead 配置、前端 smoke 脚本、downloadUrl 规则和错误码映射。
- 新增 GitHub Actions 工作流 `AICopilot Simulation Release Candidate`。
- 前端已改为后端 `downloadUrl` 下载，不再自行拼接 artifact 下载地址。
- 前端 Run Queue / Worker Status 运维信息由后端接口提供。
- 前端错误码提示覆盖发布候选要求的核心错误码。

## CI 覆盖

- backend build
- backend Simulation suites
- frontend build
- frontend unit tests
- frontend smoke
- agent simulation acceptance
- release-candidate scope guard
- encoding check
- diff check
- npm vulnerability scan

## 验证

- `npm run build`：通过。
- `npm run test:unit`：通过。
- `npm run test:smoke`：通过。
- `dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj`：通过。
- `dotnet build src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj`：通过。
- 后端 suite `AgentSimulationAcceptance`、`Batch5ApprovalHardening`、`Batch6SecretProtection`、`Batch7ReportArtifacts`、`Batch8ArtifactVersioning`、`Batch9McpToolGovernance`、`Batch10RunQueueOps`：均通过。
- `scripts/Run-AgentSimulationAcceptance.ps1`：通过，本机 Docker 可用，已运行 Docker acceptance。
- `scripts/Test-AgentWorkbenchReleaseCandidateScope.ps1 -IncludeWorkingTree`：通过。
- `scripts/Test-TextEncoding.ps1`：通过。
- `npm audit --registry=https://registry.npmjs.org --audit-level=high`：通过，0 vulnerabilities。
- `git diff --check`：通过；仅提示 `资料/A助理AgentSimulation离线验收报告.md` 下次 Git 触碰时 LF 会替换为 CRLF。

## 待完成

- 真实 CI 运行。
- Docker 不可用路径的 acceptance skip 结果记录。
- integration 分支真实前后端联调。
