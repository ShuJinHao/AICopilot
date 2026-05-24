# AICopilot 阶段记录：Real CloudReadonly Readiness P5

## 完成内容

- 新增 Real CloudReadonly Readiness 预检服务，支持 `DryRun`、`FakeEndpoint`、`RealSandboxSmoke`。
- 新增 readiness 查询、历史查询和执行入口，结果统一标记 `ReadinessOnly`。
- 固化 fake CloudAiRead contract 检查：设备、产能、设备日志、过站记录。
- Recipe 和 Recipe version 继续按 policy 阻断。
- Tool Registry 中 `query_cloud_data_readonly` 继续默认 disabled / hidden / non-executable。
- 前端 Tool Registry 页面展示 readiness gate、fake contract 检查、默认禁用状态和 token 不回显状态。

## 改动范围

- 只修改 `AICopilot`。
- 未修改 `IIoT.CloudPlatform`。
- 未修改 `IIoT.EdgeClient`。
- 未启用 Real CloudReadonly。
- 未接真实 Cloud 生产数据。

## 验证命令

```powershell
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore -v:minimal --filter "Suite=EnterpriseCloudReadonlyReadinessP5" -p:OutputPath=$PWD/artifacts/build/backendtests-p5/
```

```powershell
npm run build
```

最终验收入口：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseCloudReadonlyReadinessP5Acceptance.ps1
```

## 剩余风险

- P5 不证明真实 Cloud endpoint 已可用，只证明 readiness gate 与 fake contract 闭环可用。
- 后续 P6 如果进入 Real Sandbox，必须单独提供 endpoint、只读 token、Cloud 侧只读 contract 和 smoke 授权。
- Real Sandbox 仍不得绕过 Agent 审批、Tool Registry、CloudAiRead allowlist 和 scope guard。
