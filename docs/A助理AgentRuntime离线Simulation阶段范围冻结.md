# A 助理 Agent Runtime 离线 Simulation 阶段范围冻结

本文档冻结 Batch 0-4 的实现范围，供后续开发、验收和代码审查使用。

## 目标

本阶段只在 AICopilot 后端内完成 Agent Runtime 离线 Simulation 闭环。目标是在没有真实前端、没有真实客户端、没有真实 Cloud 的情况下，使用集中模拟数据跑通 CloudReadonly Simulation 查询、Agent 执行、审批、队列、DataWorker、报告产物、最终审批、finalize、下载和审计链路。

## 允许修改

- `src/services/AICopilot.Services.Contracts` 中 CloudReadonly 三态配置和只读数据源合同。
- `src/services/AICopilot.AiGatewayService` 中 Agent Runtime、CloudReadonly provider、Simulation 数据集、计划意图兜底。
- `src/hosts/AICopilot.HttpApi` 中 CloudReadonly 默认禁用配置和启动校验。
- `src/tests/AICopilot.BackendTests` 中后端单元测试和离线验收测试。
- `scripts` 中离线 Simulation 后端验收脚本。
- `docs` 与 `资料` 中本阶段说明和验收记录。

## 禁止修改

- 不修改 `IIoT.CloudPlatform`。
- 不修改 `IIoT.EdgeClient`。
- 不修改、格式化、暂存或提交 `src/vues`。
- 不做真实前端联调，不新增前端 smoke。
- 不引入真实 Cloud 访问默认开启。
- 不开放 shell 工具或 shell 执行能力。
- 不新增任意服务器路径写入。
- 不写真实 Cloud 业务数据。
- 不让模型自由拼 SQL。
- 不把模拟数据伪装成真实数据。
- 不为了测试通过削弱审批、权限、审计或安全边界。

## 数据来源标记

所有 Simulation 输出必须同时具备以下标记：

- `sourceMode=Simulation`
- `isSimulation=true`
- `sourceLabel=模拟 Cloud 只读数据`

这些标记必须出现在 CloudReadonly 工具结果、行数据、图表 payload、报告摘要和工具执行审计元数据中。

## 验收入口

- 静态护栏：`scripts/Test-AgentSimulationScope.ps1`
- 后端验收：`scripts/Run-AgentSimulationAcceptance.ps1`
- 验收记录：`资料/A助理AgentSimulation离线验收报告.md`
