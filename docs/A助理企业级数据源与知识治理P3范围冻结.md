# A助理企业级数据源与知识治理 P3 范围冻结

## 阶段定位

P3 只修改 `AICopilot`，目标是把 P2 的 SimulationBusiness 试用工作台从静态模板计划推进到受控动态 Planner 闭环。

本阶段不是 Real CloudReadonly 接入，不修改 `IIoT.CloudPlatform`，不修改 `IIoT.EdgeClient`，不接真实 Cloud 生产数据，不开放 shell，不允许任意服务器路径写入。

## 固定边界

- 执行业务数据源只允许 `SimulationBusiness`。
- 查询链路仍走 Text-to-SQL 只读 guardrail。
- Planner 只能建议计划步骤，不能决定安全边界。
- 后端必须二次校验工具、权限、schema、审批、数据源和 Simulation 标识。
- 后端必须补齐业务查询、查询摘要、业务图表和 final 审批等必要步骤。
- 未确认计划不得执行；驳回计划后不得调用工具。
- 不输出明文 API Key、连接串、token、密码或完整敏感 SQL。

## 本阶段能力

- `plannerMode=Auto` 默认尝试动态 Planner；无可用 Planner 模型时回退 `StaticFallback`。
- `plannerMode=DynamicOnly` 无可用 Planner 模型时返回受控错误。
- `plannerMode=StaticOnly` 明确走静态计划。
- 计划文档记录 Planner 模式、回退原因、安全摘要、强制补齐步骤、审批点和数据源摘要。
- 试用模板和自由目标都可以进入同一受控动态 Planner 链路。

## 验收入口

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseDynamicPlannerP3Acceptance.ps1
```

验收报告输出：

```text
docs/enterprise-dynamic-planner-p3-latest.md
```

## 进入下一阶段条件

- P2 继承验收通过。
- P3 动态 Planner 聚焦测试通过。
- 前端 build 通过。
- scope guard 通过。
- 报告记录动态计划、静态回退、非法计划拒绝、SimulationBusiness 标识和剩余风险。
