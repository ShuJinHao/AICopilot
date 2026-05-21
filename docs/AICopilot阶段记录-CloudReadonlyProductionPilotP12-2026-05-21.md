# AICopilot 阶段记录 - CloudReadonly Production Pilot P12

## 范围

- 仅修改 `AICopilot`
- 新增 P12 Production Pilot 配置、工具边界、服务、端点、前端面板、focused tests 和验收入口
- Cloud/Edge 未纳入本阶段修改范围

## 关键结果

- 新增 `CloudReadonlyProductionPilot` 默认关闭配置段
- 新增受保护工具 `query_cloud_production_pilot_readonly`，默认 disabled / hidden / non-executable
- 新增 P12 Pilot Window、gate、固定模板场景命令和查询结果标识
- 前端试用运营页增加 P12 生产只读 Pilot 面板
- 新增 `Run-EnterpriseCloudReadonlyProductionPilotP12Acceptance.ps1`

## 安全边界

- 生产只读 Pilot 仅允许四类端点：`devices`、`capacity_summary`、`device_logs`、`pass_station_records`
- Recipe、Recipe version、写路径、未知端点、超时间范围、超行数均阻断
- 审计和报告不输出 token、API Key、连接串、完整 payload 或完整敏感上下文
