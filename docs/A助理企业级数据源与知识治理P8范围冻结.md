# A助理企业级数据源与知识治理 P8 范围冻结

- 阶段：P8 CloudReadonly Sandbox 受控扩展试用。
- 修改范围：仅 `AICopilot`。
- 禁止范围：不修改 `IIoT.CloudPlatform`、`IIoT.EdgeClient`；不接真实生产数据；不启用 Real CloudReadonly；不开放生产 Cloud 查询工具。
- 默认关闭项：`CloudReadonly.Mode=Disabled`、`CloudReadonly.Real.Enabled=false`、`CloudReadonly.Real.AllowProductionRead=false`、`CloudAiRead.Enabled=false`、`CloudReadonlySandboxControlledTrial.Enabled=false`。
- 试用边界：允许受控自由目标，但必须先生成 `CloudSandboxGoalIntent`，再由后端映射到 sandbox allowlist endpoint。
- Endpoint 边界：仅 `devices`、`capacity_summary`、`device_logs`、`pass_station_records`；Recipe、Recipe version、写路径、未知 endpoint、生产路径继续 `BlockedByPolicy`。
- Sandbox 标识：`sourceMode=CloudReadonlySandbox`、`isSandbox=true`、`isSimulation=false`、`sourceLabel=Cloud 只读 Sandbox（非生产）`、`boundary=SandboxControlledTrial`。
- 生产工具边界：`query_cloud_data_readonly` 必须继续 disabled / hidden / non-executable。
- Sandbox 工具边界：P8 继续使用 `query_cloud_sandbox_readonly`，仅在 sandbox smoke、P7 fixed trial、P8 controlled trial、权限、计划确认和审批满足后执行。
- 审计边界：记录 endpoint code、intent hash、状态、耗时、行数、截断、hash、错误码；不记录 token、连接串、完整 payload 或敏感上下文。
