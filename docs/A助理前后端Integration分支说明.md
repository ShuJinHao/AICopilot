# A助理前后端 Integration 分支说明

日期：2026-05-18

## 分支

建议分支：`integration/aicopilot-agent-workbench-simulation`

合并来源固定为：

```text
main
+ 后端 Simulation Runtime PR #46
+ 前端 Agent Workbench PR
```

## 运行边界

- Integration 分支只用于联调和验收，不直接代表最终主线。
- 不接真实 Cloud 生产数据。
- `CloudReadonly.Mode` 默认仍保持 `Disabled`，联调运行时通过环境变量或联调配置显式启用 `Simulation`。
- `CloudAiRead.Enabled=false` 默认不变。
- `CloudReadonly.Real.Enabled=false` 和 `CloudReadonly.Real.AllowProductionRead=false` 默认不变。
- Cloud/Edge 仓库已有脏改不纳入本分支。

## 联调配置

```json
{
  "CloudReadonly": {
    "Mode": "Simulation",
    "Simulation": {
      "Enabled": true,
      "SeedData": true,
      "DataSet": "ManufacturingDemo",
      "AlwaysMarkAsSimulation": true
    },
    "Real": {
      "Enabled": false,
      "AllowProductionRead": false
    }
  },
  "CloudAiRead": {
    "Enabled": false
  }
}
```

## 验收入口

- 前端：`npm run build`、`npm run test:unit`、`npm run test:smoke`
- 后端：`scripts/Run-AgentSimulationAcceptance.ps1`
- Scope guard：`scripts/Test-AgentWorkbenchReleaseCandidateScope.ps1`
