# AICopilot 阶段记录：Real CloudReadonly Sandbox Agent Trial P7

- 日期：2026-05-20
- 范围：仅 AICopilot。
- 目标：在 P6 sandbox smoke 基础上，增加固定模板 Agent Sandbox Trial 受控试用闭环。

## 已冻结边界

- Real CloudReadonly 仍默认关闭。
- `query_cloud_data_readonly` 仍 disabled / hidden / non-executable。
- 新增 `query_cloud_sandbox_readonly`，使用 `CloudReadonlySandboxOnly` 数据边界和 `SandboxAgentTrial` 审批策略。
- `CloudReadonlySandboxAgentTrial.Enabled=false` 为默认值，sandbox trial 不会默认进入 Agent Runtime。
- Sandbox trial 结果统一标识为 `CloudReadonlySandbox` 和 `Cloud 只读 Sandbox（非生产）`。

## 固定模板

- 设备清单
- 产能汇总
- 设备日志
- 过站记录
- 设备异常分析
- 产能交付分析

## 验收入口

- `scripts/Run-EnterpriseCloudReadonlySandboxAgentTrialP7Acceptance.ps1`
- 输出：`docs/enterprise-cloud-readonly-sandbox-agent-trial-p7-latest.md`

