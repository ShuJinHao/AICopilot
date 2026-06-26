# A助理真实 CloudReadonly 准备说明

日期：2026-05-18

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
