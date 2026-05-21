# A助理企业级数据源与知识治理 P7 范围冻结

- 阶段：P7 Real CloudReadonly Sandbox Agent Trial 受控试用。
- 修改范围：仅 `AICopilot`。
- 禁止范围：不修改 `IIoT.CloudPlatform`、`IIoT.EdgeClient`；不接真实生产数据；不启用 Real CloudReadonly；不开放生产 Cloud 查询工具。
- 默认关闭项：`CloudReadonly.Mode=Disabled`、`CloudReadonly.Real.Enabled=false`、`CloudReadonly.Real.AllowProductionRead=false`、`CloudAiRead.Enabled=false`、`CloudReadonlySandboxAgentTrial.Enabled=false`。
- 试用边界：仅固定 Cloud Sandbox Trial 模板；不开放自由 Agent 目标；不复用 `query_cloud_data_readonly`。
- Sandbox 标识：`sourceMode=CloudReadonlySandbox`、`isSandbox=true`、`isSimulation=false`、`sourceLabel=Cloud 只读 Sandbox（非生产）`、`boundary=SandboxAgentTrial`。
- 生产工具边界：`query_cloud_data_readonly` 必须继续 disabled / hidden / non-executable。
- Sandbox 工具边界：`query_cloud_sandbox_readonly` 仅在 sandbox smoke 通过、trial 开关开启、固定模板、权限和审批满足后执行。
- 审计边界：记录 endpoint code、状态、耗时、行数、截断、hash、错误码；不记录 token、连接串、完整 payload 或敏感上下文。

