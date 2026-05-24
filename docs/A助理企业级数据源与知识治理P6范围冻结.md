# A助理企业级数据源与知识治理 P6 范围冻结

## 阶段目标

P6 只做 Real CloudReadonly sandbox/staging 只读 smoke 预接入。目标是验证真实 sandbox 端点、token、allowlist、返回结构、脱敏、错误码和审计形态，为 P7 受控 Agent Sandbox Trial 提供进入依据。

## 固定边界

- 只修改 `AICopilot`。
- 不修改 `IIoT.CloudPlatform`、`IIoT.EdgeClient`。
- 不接真实 Cloud 生产数据。
- 不默认启用 Real CloudReadonly。
- 不设置 `CloudReadonly.Mode=Real`。
- 不设置 `CloudReadonly.Real.Enabled=true`。
- 不设置 `CloudReadonly.Real.AllowProductionRead=true`。
- 不设置 `CloudAiRead.Enabled=true`。
- 不开放 `query_cloud_data_readonly` 给 Planner 或 Agent。
- 不生成真实 Cloud 数据产物。
- 不回显 token、API Key、连接串、密码或完整敏感 payload。

## P6 新边界

P6 新增独立配置段 `CloudReadonlySandbox`。该配置只用于 `RealSandboxSmoke` readiness 检查，不复用 Real CloudReadonly 生产读取配置，也不要求 `AllowProductionRead=true`。

所有 sandbox smoke 结果必须标记：

```text
boundary=SandboxSmokeOnly
```

`CloudReadonlyReadiness` 总体预检仍保留：

```text
boundary=ReadinessOnly
```

## 允许的 Sandbox Smoke 端点

- `devices`
- `capacity_summary`
- `device_logs`
- `pass_station_records`

以下能力继续禁读或拒绝：

- Recipe 主数据
- Recipe version history
- 写路径
- 未知路径
- 未经 allowlist 的 POST
- 含写语义的路径

## 验收入口

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseCloudReadonlySandboxP6Acceptance.ps1
```

报告输出：

```text
docs/enterprise-cloud-readonly-sandbox-p6-latest.md
```

## 完成条件

- 默认配置仍全部关闭。
- Fake sandbox contract 和 P6 后端测试通过。
- Sandbox smoke 使用 `CloudReadonlySandbox`，不依赖 Real CloudReadonly 或 CloudAiRead 生产开关。
- Token、连接串和完整 payload 不出现在 API、日志、前端或验收报告中。
- Tool Registry 证明 `query_cloud_data_readonly` 仍 disabled / hidden / non-executable。
- 前端 Readiness 面板显示 sandbox smoke 状态，但不误展示成生产数据接入。
