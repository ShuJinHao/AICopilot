# A助理真实 CloudReadonly 准备说明

日期：2026-05-18

> 历史记录：本文记录 2026-05 阶段的 CloudReadonly / Cloud AiRead 准备口径。当前执行口径是高频设备日志、小时/汇总产能和生产数据优先走 Cloud AiRead 正式只读 API；DataAnalysis `CloudReadOnly` Direct DB / Text-to-SQL 只作为低频探索和治理白名单内补充分析兜底。当前执行入口以 `../AGENTS.md`、`资料/AICopilot业务规则.md` 和 `docs/改动复盘与规则沉淀.md` 为准。

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

本段为历史准备口径。当前如果 CloudAiRead 已启用，高频设备日志、小时/汇总产能和生产数据必须走 Cloud AiRead；Direct DB / Text-to-SQL 只保留为低频探索、治理白名单内补充分析或未覆盖只读链路兜底。

## Readiness 内容

- CloudAiRead connectivity test。
- Cloud AiRead / P12 / P13 受控入口验收。
- 只读 intent 白名单。
- Recipe 主数据和版本继续禁止。
- 查询时间范围、返回行数、请求频率限制。
- Cloud 查询审计和错误码映射。
- Cloud AiRead 当前参数契约测试：`deviceId`、`startDate`/`endDate`、`startTime`/`endTime` 或 `preset`、小时产能 `date`/`preset`、生产记录 `typeKey`/`processId`/`deviceId`、日志 `minLevel`。
- 数据脱敏策略。
- 前端真实 Cloud 只读验收状态和 Simulation 历史材料区分。

## 灰度路线

```text
Disabled -> CloudAiRead Contract -> P12/P13 Controlled Pilot -> Production Acceptance
```

不得跳过中间阶段。
