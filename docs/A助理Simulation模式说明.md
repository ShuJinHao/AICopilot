# A助理 Simulation 模式说明

日期：2026-05-18

> 当前口径：Simulation 保留为显式离线演示/测试资产，默认关闭；不得作为 Real Cloud / Direct DB / AiRead 的 fallback，也不得在真实数据为空或配置失败时补位。

## 定义

Simulation 模式用于在不接真实 Cloud 的情况下验证 Agent Runtime、CloudReadonly 工具、审批、队列、DataWorker、产物和审计闭环。

## 标识

所有 Simulation 输出必须包含：

```text
sourceMode=Simulation
isSimulation=true
sourceLabel=模拟 Cloud 只读数据
```

这些标识必须出现在工具结果、图表 payload、报告摘要、artifact、审计和前端展示中。

## 禁止事项

- 不把 Simulation 数据展示成真实 Cloud 数据。
- 不让 Simulation 配置成为默认生产配置。
- 不通过 MCP、Tool、Agent workflow、后台任务或隐藏适配器写入 Cloud。
- 不绕过 Tool Registry、审批、权限和审计。
