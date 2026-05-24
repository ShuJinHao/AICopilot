# A助理企业级数据源与知识治理 P9 范围冻结

## 阶段目标

P9 只建设 AICopilot 内部的 Artifact Workspace 产物交付中心闭环，将 Agent 草稿产物推进到可预览、可修订、可重新生成草稿版本、可提交 final 审批、审批后锁定和可审计。

## 范围边界

- 只修改 `AICopilot`。
- 不修改 `IIoT.CloudPlatform`。
- 不修改 `IIoT.EdgeClient`。
- 不启用 Real CloudReadonly。
- 不接真实 Cloud 生产数据。
- 不开放生产 Agent 查询。
- 不做完整网盘、多人协同编辑或任意服务器路径文件管理。

## 允许来源

P9 产物只承接已受控的数据来源：

- `SimulationBusiness`
- `CloudReadonlySandbox`

每个产物必须保留以下标识：

- `sourceMode`
- `boundary`
- `isSimulation`
- `isSandbox`
- `sourceLabel`
- `queryHash`
- `resultHash`
- `rowCount`
- `isTruncated`
- `approvalStatus`

## 产物生命周期

P9 使用以下工作区语义：

- `Draft`：草稿产物，可在 final 审批前修订和重新生成。
- `FinalPendingApproval`：已进入 final 审批或 artifact 审批窗口。
- `Final`：审批通过并发布到 final 区，只读锁定。
- `Deleted`：软删除或不可预览状态。

草稿重新生成必须递增版本，不得覆盖已 final 产物。final 通过后不得静默替换、重写或覆盖。

## 安全要求

- 预览接口只通过受控 artifact id 访问，不接收任意服务器路径。
- 审计记录 artifact id/version、source mode、hash、状态、用户、耗时和错误码。
- 审计和报告不得输出 API Key、token、连接串、完整 SQL 或完整 sandbox payload。
- P9 验收必须继续证明 `query_cloud_data_readonly` 默认关闭。
