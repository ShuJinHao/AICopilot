# AICopilot 阶段记录：CloudReadonly Sandbox 受控扩展试用 P8

## 阶段目标

P8 将 P7 的固定模板 Sandbox Agent Trial 扩展为受控自由目标 Sandbox Trial。用户可以输入自由业务目标，但后端必须先生成 `CloudSandboxGoalIntent`，并且只能映射到 sandbox allowlist endpoint。

## 实现摘要

- 新增 `CloudReadonlySandboxControlledTrial` 配置段，默认 `Enabled=false`。
- 新增 `CloudReadonlySandboxControlledTrialService`，负责 gate 状态、自由目标 intent、endpoint allowlist、时间范围、行数、产物类型和执行边界。
- 新增 `CreateCloudReadonlySandboxControlledPlanCommand`，返回待确认 Agent 计划和 `CloudSandboxGoalIntent`。
- Agent 计划文档新增 `isCloudSandboxControlledTrial` 和 `cloudSandboxGoalIntent`。
- Agent Runtime 在 `query_cloud_sandbox_readonly` 下支持 `SandboxControlledTrial`，产物继续显示 sandbox 非生产标识、endpoint、hash、行数、截断和审批状态。
- 前端 Agent 工作台新增 Cloud Sandbox Controlled Trial 自由目标入口；Tool Registry 展示 P8 gate 状态。
- 验收入口新增 `scripts/Run-EnterpriseCloudReadonlySandboxExpansionP8Acceptance.ps1`。

## 安全边界

- 未修改 `IIoT.CloudPlatform` 和 `IIoT.EdgeClient`。
- 未启用 Real CloudReadonly。
- 未接真实 Cloud 生产数据。
- `query_cloud_data_readonly` 继续 disabled / hidden / non-executable。
- P8 只允许 `devices`、`capacity_summary`、`device_logs`、`pass_station_records`。
- Recipe、Recipe version、写路径、生产路径和未知 endpoint 均按策略拒绝。
- API Key、token、连接串、完整 payload 不进入接口回显、产物或验收报告。

## 验证命令

```powershell
dotnet build src/services/AICopilot.AiGatewayService/AICopilot.AiGatewayService.csproj /m:1 /p:UseSharedCompilation=false
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Suite=EnterpriseCloudReadonlySandboxExpansionP8" /m:1 /p:UseSharedCompilation=false
dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj /m:1 /p:UseSharedCompilation=false
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Suite=EnterpriseCloudReadonlySandboxAgentTrialP7|Suite=FrontendIntegrationContract" /m:1 /p:UseSharedCompilation=false
powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseCloudReadonlySandboxExpansionP8Acceptance.ps1 -SkipFrontend -SkipInheritedP7
cd src/vues/AICopilot.Web
npm run build
```

## 剩余风险

- 当前核心验收仍使用 fake sandbox client 和 contract fixtures，不证明生产 Cloud 读取可用。
- 真实 sandbox endpoint/token 后续只能作为可选输入进入 `SandboxControlledTrial`，不得改变默认关闭边界。
- P8 不开放生产自由查询、不做 Cloud/Edge 联动、不做旧接口兼容层。
