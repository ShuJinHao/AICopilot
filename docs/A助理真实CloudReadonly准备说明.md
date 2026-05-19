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
CloudReadonly.Real.Enabled=false
CloudReadonly.Real.AllowProductionRead=false
```

## Readiness 内容

- CloudAiRead connectivity test。
- Real provider sandbox 验收。
- 只读 intent 白名单。
- Recipe 主数据和版本继续禁止。
- 查询时间范围、返回行数、请求频率限制。
- Cloud 查询审计和错误码映射。
- Real / Simulation 数据结构对齐测试。
- 数据脱敏策略。
- 前端 Real / Simulation 标签区分。

## 灰度路线

```text
Disabled -> Simulation -> Real Sandbox -> Real Restricted Internal -> Real Pilot -> Production
```

不得跳过中间阶段。
