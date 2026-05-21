# AICopilot 阶段记录 - CloudReadonly Production Controlled Pilot P13

## 目标

在 P12 固定模板生产只读 Pilot 的基础上，增加 P13 受控自由目标 Pilot。用户输入目标后，后端先生成 `CloudProductionGoalIntent`，再进入计划、审批、只读查询、草稿产物、final 审批和审计闭环。

## 边界

- 只修改 `AICopilot`
- 不修改 Cloud/Edge
- 不开放 `query_cloud_data_readonly`
- 不开放自由生产 SQL
- 不开放 Cloud 写
- 默认 `CloudReadonlyProductionControlledPilot.Enabled=false`
- 默认 `CloudReadonlyProductionControlledPilot.FreeGoalEnabled=false`

## 新增能力

- P13 配置段和默认关闭校验
- `CloudProductionGoalIntent` 受控映射
- `query_cloud_production_controlled_readonly` 受保护工具定义
- P13 readiness/status、plan、run 接口
- Agent Runtime P13 专用工具执行路径
- Agent 工作台 P13 Controlled Pilot 入口
- P13 后端测试、前端 smoke、scope guard 和 acceptance 脚本

## 验收

聚焦验收命令：

```powershell
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Suite=EnterpriseCloudReadonlyProductionControlledPilotP13" /m:1 /p:UseSharedCompilation=false
```

完整验收命令：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseCloudReadonlyProductionControlledP13Acceptance.ps1
```
