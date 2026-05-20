# AICopilot 阶段记录：Trial Operations P10

## 范围

- 仅修改 `AICopilot`。
- 不启用 Real CloudReadonly，不接真实生产数据。
- 不开放生产 CloudReadonly 工具给 Planner/Agent。

## 已实现

- 新增 Trial Campaign / Scenario Run / Risk Issue 聚合与 EF 持久化。
- 新增 P10 命令/查询与 HTTP API：
  - Campaign 创建、状态流转、列表、详情。
  - Agent task 证据挂载。
  - 风险台账 upsert。
  - Pilot Readiness 评估。
  - Evidence Package 生成。
- 新增 P10 权限：
  - `AiGateway.TrialOperations.Read`
  - `AiGateway.TrialOperations.Manage`
  - `AiGateway.TrialOperations.AuditView`
- 前端 Agent 工作台新增“试用”页签，展示 Campaign、source boundary、场景运行、readiness checks 和 evidence metrics。
- Scope guard 扩展 P10 marker 检查。
- 新增 P10 验收入口 `scripts/Run-EnterpriseTrialOperationsP10Acceptance.ps1`。

## 安全边界

- Trial Campaign 只允许 `SimulationBusiness` 和 `CloudReadonlySandbox` source mode。
- 试用台账只保存任务、产物、hash、source mode、审批状态和审计摘要引用。
- Evidence Package 不保存或输出完整 SQL、完整 payload、token、API Key、连接串或密码。
- `ReadyForP11Planning` 只表示可以规划 P11，不表示已经启用生产数据。

## 验证

- `dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj`
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Suite=EnterpriseTrialOperationsP10"`
- `npm run build` in `src/vues/AICopilot.Web`
- `scripts/Run-EnterpriseTrialOperationsP10Acceptance.ps1`
