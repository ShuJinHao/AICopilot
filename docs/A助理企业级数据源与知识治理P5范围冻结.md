# A助理企业级数据源与知识治理 P5 范围冻结

## 阶段定位

P5 只修改 `AICopilot`，目标是完成 Real CloudReadonly Readiness 预检闭环。
本阶段不是正式接入真实 Cloud 生产数据，不修改 `IIoT.CloudPlatform`，不修改 `IIoT.EdgeClient`，不默认启用 Real CloudReadonly，不让 Agent 直接读取真实 Cloud。

## 固定边界

- 默认配置必须继续保持 `CloudReadonly.Mode=Disabled`。
- 默认配置必须继续保持 `CloudReadonly.Real.Enabled=false`。
- 默认配置必须继续保持 `CloudReadonly.Real.AllowProductionRead=false`。
- 默认配置必须继续保持 `CloudAiRead.Enabled=false`。
- `query_cloud_data_readonly` 默认必须 disabled / hidden / non-executable。
- P5 核心验收只使用 fake CloudAiRead contract fixtures。
- 如果后续配置真实 staging endpoint，也只能走 `RealSandboxSmoke` readiness，不进入 Agent Runtime。
- 不输出明文 API Key、连接串、token、密码或完整敏感 payload。

## 本阶段能力

- 新增 Real CloudReadonly Readiness 查询、历史和执行入口。
- 支持 `DryRun`、`FakeEndpoint`、`RealSandboxSmoke` 三种 readiness 模式，默认使用 `FakeEndpoint`。
- 固化四类 AI-facing 只读 contract preflight：
  - `devices`
  - `capacity_summary`
  - `device_logs`
  - `pass_station_records`
- Recipe 和 Recipe version 继续按 policy 阻断。
- Readiness 结果统一标记 `Boundary=ReadinessOnly`。
- 前端 Tool Registry 配置页展示 ReadinessOnly 状态、fake contract 检查、默认禁用边界和最近检查历史数量。

## 验收入口

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseCloudReadonlyReadinessP5Acceptance.ps1
```

验收报告输出：

```text
docs/enterprise-cloud-readonly-readiness-p5-latest.md
```

## 进入下一阶段条件

- P4 继承验收通过。
- P5 scope guard 通过，确认 Cloud/Edge 未改、Real CloudReadonly 默认禁用、CloudAiRead 默认禁用。
- P5 fake CloudAiRead contract tests 通过。
- 前端 build 和 `/config` smoke 通过。
- 报告记录 readiness gate、fake contract、拒绝样例、Tool Registry 未开放证明和剩余风险。
