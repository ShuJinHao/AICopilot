# A助理总纲对齐 P2 范围冻结

## 边界

- 本阶段只修改 AICopilot。
- 不修改 IIoT.CloudPlatform。
- 不修改 IIoT.EdgeClient。
- 不启用 Real CloudReadonly。
- 不接真实 Cloud 生产数据。
- 不开放 shell、任意服务器路径写入或危险 SQL。
- 不输出明文 API Key、连接串、token、密码。

## 目标

P2 对齐总纲里的企业级受控产物 Agent 工作台，但执行数据源限定为 SimulationBusiness。

内部试用闭环为：

1. 用户选择固定业务试用模板。
2. 后端生成受控 Agent 计划预览。
3. 用户确认计划。
4. Agent 调用 Text-to-SQL 只读链路查询 AI 独立模拟业务库。
5. 生成图表、Markdown、HTML、PDF、PPTX、XLSX 草稿。
6. 进入 final 审批。
7. 审计记录 query hash、Simulation 标识、产物和审批状态。

## 明确不做

- 不做真实 CloudReadonly 闭环。
- 不做 Cloud/Edge 联动。
- 不做完整动态 Planner。
- 不做 MCP 工具库深度治理。
- 不要求真实模型 API Key。
- 不把模拟业务库伪装成真实业务系统。

## P2 完成条件

- 六个试用模板可由后端返回：产能分析、质量缺陷、设备停机、库存周转、销售交付、员工制度/RAG 补充说明。
- 模板创建任务后处于待计划确认状态，未确认不得执行。
- 查询结果和产物必须包含 `sourceMode=SimulationBusiness`、`isSimulation=true`、`sourceLabel=AI 独立模拟业务库`、`queryHash`。
- 前端工作台展示模板、计划、步骤、审批、产物、审计，并显示 SimulationBusiness 试用边界。
- P2 验收脚本生成 `docs/enterprise-agent-workbench-p2-latest.md`。
