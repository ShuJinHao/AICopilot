# A助理真实 CloudReadonly 准备说明

日期：2026-05-18

> 历史记录：本文记录 2026-05 阶段的 CloudReadonly / Cloud AiRead 准备口径。当前内部真实 Cloud 查询优先路线是 DataAnalysis `CloudReadOnly` Direct DB；Cloud AiRead 仅封存为未来外部系统只读 API 接入口。当前执行入口以 `../AGENTS.md`、`资料/AICopilot业务规则.md` 和 `docs/改动复盘与规则沉淀.md` 为准。

## 启动条件

只有满足以下条件后，才进入真实 CloudReadonly Readiness：

- Simulation 前后端联调通过。
- 发布候选硬化通过。
- 前端工作台稳定。
- Cloud 侧提供稳定、明确、只读的 AI-facing 输出。
- 用户明确要求开始真实 CloudReadonly 准备。

## 默认保持

```text
CloudReadonly.Mode=Disabled
CloudAiRead.Enabled=false
```

内部 Direct DB 验证启用时，`CloudAiRead.Enabled` 仍应保持 `false`，避免旧 AiRead 路线压过 Direct DB。即便未来外部系统启用 AiRead，内部语义映射存在时也必须优先使用 Direct DB。

## Readiness 内容

- CloudAiRead connectivity test。
- Cloud AiRead / P12 / P13 受控入口验收。
- 只读 intent 白名单。
- Recipe 主数据和版本继续禁止。
- 查询时间范围、返回行数、请求频率限制。
- Cloud 查询审计和错误码映射。
- Cloud AiRead 当前参数契约测试：`deviceId`、`startDate`/`endDate`、`startTime`/`endTime`、显式 `passStationTypeKey`。
- 数据脱敏策略。
- 前端真实 Cloud 只读验收状态和 Simulation 历史材料区分。

## 灰度路线

```text
Disabled -> CloudAiRead Contract -> P12/P13 Controlled Pilot -> Production Acceptance
```

不得跳过中间阶段。
