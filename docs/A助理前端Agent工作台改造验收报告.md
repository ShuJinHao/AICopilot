# A助理前端 Agent 工作台改造验收报告

日期：2026-05-18

## 范围

本阶段只收口 `AICopilot/src/vues/AICopilot.Web` 前端工作台、前端测试和 AICopilot 交付文档。不修改 `IIoT.CloudPlatform`、`IIoT.EdgeClient`、`IIoT.EdgeClient.AvaloniaMigration`。

## 已完成

- Agent 工作台已覆盖会话、模型选择、上传、任务计划、步骤、审批、产物、审计和 Cloud 只读边界视图。
- 产物下载改为只使用后端返回的 `downloadUrl`，前端不再从 artifact id 自行拼接下载地址。
- 运行、重试、提交最终审批、最终输出按钮使用后端返回的 `canRun`、`canRetry`、`canSubmitFinalReview`、`canApproveFinal` 和队列状态控制。
- 配置页 Agent 标签补充 Run Queue 和 Worker Status 运维视图，展示后端返回的队列统计、队列项、Worker 心跳和 `workspaceMatchesHttpApi`。
- 前端错误提示覆盖 Simulation 发布候选要求中的权限、工具、队列、worker、artifact、workspace 和 planner 错误码。
- smoke mock 补充 Simulation source marker、Run Queue、Worker Status 和后端计算字段。

## 验证

- `npm run build`：通过。
- `npm run test:unit`：通过，10 个测试文件、39 个测试。
- `npm run test:smoke`：通过，16 passed、2 skipped。

## 待验收

- 用真实后端运行完整 Simulation UI 链路。
