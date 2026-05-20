# AICopilot 阶段记录：CloudReadonly Pilot Readiness P11

## 完成内容

- 新增 P11 范围冻结文档，明确 P11 是 Pilot 准入演练，不读取真实生产数据。
- 新增 `CloudReadonlyPilotReadiness` 配置段，默认 `Enabled=false`。
- 新增 P11 DTO、命令、查询与服务：
  - `CloudReadonlyPilotReadinessStatusDto`
  - `CloudReadonlyPilotConfigPackageDto`
  - `PilotApprovalRehearsalDto`
  - `CreateCloudReadonlyPilotConfigPackageCommand`
  - `RunCloudReadonlyPilotGateEvaluationCommand`
  - `RunCloudReadonlyPilotApprovalRehearsalCommand`
  - `RunCloudReadonlyPilotContractRehearsalCommand`
  - `GetCloudReadonlyPilotReadinessQuery`
- 新增只读工具目录描述 `query_cloud_pilot_readiness_readonly`，默认 disabled / hidden / non-executable。
- 前端 Agent 试用面板新增 P11 Pilot 准入演练区域，显示配置包、gate、审批演练、fake contract 和无生产读取标识。
- Scope guard 扩展 P11 marker 检查。
- 新增 P11 验收入口 `scripts/Run-EnterpriseCloudReadonlyPilotReadinessP11Acceptance.ps1`。

## 安全边界

- `CloudReadonly.Mode=Disabled` 保持默认。
- `CloudReadonly.Real.Enabled=false` 保持默认。
- `CloudReadonly.Real.AllowProductionRead=false` 保持默认。
- `CloudAiRead.Enabled=false` 保持默认。
- `query_cloud_data_readonly` 继续 disabled / hidden / non-executable。
- P11 fake contract 结果统一标识为 `sourceMode=CloudReadonlyPilotReadiness`、`boundary=PilotReadinessRehearsal`、`isProductionData=false`。
- 审计和报告只记录配置、状态、endpoint code、hash、行数和错误码，不输出 token、连接串、完整 SQL 或完整 payload。

## 验证命令

已执行并通过：

```powershell
dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj /m:1 /p:UseSharedCompilation=false -o "$env:TEMP\aicopilot-p11-build-httpapi"
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Suite=EnterpriseCloudReadonlyPilotReadinessP11" /m:1 /p:UseSharedCompilation=false -o "$env:TEMP\aicopilot-p11-test"
powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
npm run build
dotnet build src/hosts/AICopilot.AppHost/AICopilot.AppHost.csproj /m:1 /p:UseSharedCompilation=false
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName~CloudReadonlyReadinessRoutes_ShouldBeRoutable" /m:1 /p:UseSharedCompilation=false
powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseCloudReadonlyPilotReadinessP11Acceptance.ps1 -SkipInheritedP10
```

最新验收报告：`docs/enterprise-cloud-readonly-pilot-readiness-p11-latest.md`。

## 剩余风险

- P11 不证明真实 Cloud 生产端点可用。
- P11 不授权真实生产读取。
- 后续进入真实生产只读 Pilot 需要单独 P12 或专项授权计划。
