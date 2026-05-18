# A助理 Agent 工作台 Simulation 前后端联调验收报告

日期：2026-05-18

## 验收目标

用真实前端页面和 AICopilot 后端 Simulation Runtime 跑通 Agent 工作台产品闭环。

## 必验场景

- 会话创建、模型选择、普通消息发送、历史消息恢复。
- 上传文件、选择知识库、创建 `CloudDataReport` Agent 任务。
- 展示 `plannerMode`、`riskLevel`、`steps`、`toolCode`、`requiresApproval`、`cloudReadonlyIntent`。
- Plan approval、ToolCall approval、FinalOutput approval 均进入后端审批链路。
- 任务入队后展示 `runQueueStatus`、`isRunQueued`、`isRunInProgress`。
- DataWorker 执行后生成 `charts/chart-data.json`、`draft/report.md`、`draft/report.html`、`draft/report.pdf`、`draft/report.pptx`、`draft/report.xlsx`。
- 图表、报告、审计和 tool execution 均展示 `sourceMode=Simulation`、`isSimulation=true`、`sourceLabel=模拟 Cloud 只读数据`。
- 文本类 draft artifact 支持编辑、版本、diff、restore；final review 之后禁止继续编辑。
- final artifact 只能通过后端 `downloadUrl` 下载。
- Run Queue、Worker Status、MCP、Tool Registry、审计视图可查看。

## 当前状态

- 前端构建通过。
- 前端 unit 和 smoke 通过。
- 后端 Simulation acceptance 与发布候选硬化 suite 通过。
- smoke mock 已覆盖 Run Queue、Worker Status 和 Simulation 标签。
- 真实后端联调和人工验收尚待执行。
