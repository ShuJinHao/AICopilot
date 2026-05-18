# A助理 Agent 工作台内部试用报告

日期：2026-05-18

## 状态

内部试用尚未开始。本文件用于记录进入试用后的指标和反馈。

## 试用边界

- 仅小范围内部试用。
- 只使用 Simulation 或后续明确批准的 Real Sandbox。
- 不接真实生产 Cloud 数据。
- 不开放 shell。

## 角色

- Admin：配置模型、工具、权限、队列。
- AgentApprover：审批 ToolCall / FinalOutput。
- User：创建任务、上传文件、查看产物、提交 final review。
- Viewer：查看报告和 workspace。
- Operator：查看队列、worker、审计。

## 指标

- 任务创建数、完成数、失败数。
- 平均任务耗时、平均审批等待时间。
- 工具调用成功率。
- PDF/PPTX/XLSX 生成成功率。
- Artifact 下载成功率、Artifact 修改次数。
- Final approval 通过率和驳回率。
- Run queue failed 数、Dead-letter 数。
- Worker 可用率。
- 模型调用次数和成本。
- 用户反馈。
