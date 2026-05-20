# AICopilot 阶段记录：Real CloudReadonly Sandbox P6

## 阶段

P6：Real CloudReadonly Sandbox Smoke 受控预接入。

## 修改范围

仅修改 `AICopilot`。本阶段未修改 `IIoT.CloudPlatform`、`IIoT.EdgeClient`，未接入真实生产数据，未开放 Agent Runtime 读取真实 Cloud 数据。

## 主要实现

- 新增 `CloudReadonlySandbox` 独立配置段，默认关闭。
- 新增 sandbox-only HTTP client，用于 readiness smoke，不复用 Real CloudReadonly 或 CloudAiRead 生产开关。
- 扩展 readiness DTO、查询和历史接口，输出 `SandboxSmokeOnly` 状态。
- `RealSandboxSmoke` 只允许 devices、capacity summary、device logs、pass-station records 四类只读端点。
- Recipe、Recipe version、写路径和未知路径继续 `BlockedByPolicy`。
- Tool Registry 继续证明 `query_cloud_data_readonly` 默认 disabled / hidden / non-executable。
- 前端 Tool Registry 配置页新增 Sandbox Smoke 状态、最近结果和阻塞项展示，不回显 token。
- 新增 P6 scope guard、后端测试和验收入口。

## 验收入口

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseCloudReadonlySandboxP6Acceptance.ps1
```

## 安全边界

- `CloudReadonly.Mode=Disabled`
- `CloudReadonly.Real.Enabled=false`
- `CloudReadonly.Real.AllowProductionRead=false`
- `CloudAiRead.Enabled=false`
- `CloudReadonlySandbox.Enabled=false`
- `query_cloud_data_readonly` 不进入 Planner catalog，不允许 Agent 执行
- 审计和报告只记录 endpoint code、status、duration、row count、truncated、result hash、error code
- 不记录 token、API Key、连接串、密码或完整 payload

## 后续进入 P7 的条件

P7 如要做受控 Agent Sandbox Trial，必须单独建立审批和运行边界，并继续保持与生产读取分离。P6 smoke 通过不自动开放 Planner/Agent 工具。
