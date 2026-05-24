# AICopilot M7 真实 Pilot 前硬停授权包

版本：2026-05-24

## 当前结论

M7 不在本轮执行真实 Pilot。本文件只定义进入真实 Pilot 前必须补齐的授权材料和硬停条件。

## 必须补齐的授权材料

- 明确 Pilot 业务范围、部门、负责人、执行窗口和回滚窗口。
- 明确数据 owner、工具 owner、最终输出 owner、rollback owner、emergency owner。
- 明确允许 endpoint，且只能来自 `devices`、`capacity_summary`、`device_logs`、`pass_station_records`。
- 明确 `maxRows <= 50`、`timeRangeDays <= 7`。
- 明确凭据配置责任人和密钥保存位置，但不得把 endpoint/token/API Key/connection string 写入仓库、报告、日志、审计或 DTO。
- 明确 emergency stop 验证方式和回滚责任人。
- 明确 post-run audit archive 的安全摘要格式。

## 硬停条件

出现任一情况，M7 不得执行：

- 未获得用户单独明确授权真实 Pilot。
- 未提供正式签署/确认的授权材料。
- 请求 Cloud 写、Recipe/version、自由 SQL 或未批准 endpoint。
- 要求输出 raw payload、raw business rows、full SQL、token、API Key、connection string。
- 要求把 `query_cloud_data_readonly` 从 disabled/hidden/non-executable 直接开放为可执行。
- 要求声明 GA 或对外发布。

## 本轮状态

- `ExecutionPermission=not granted`。
- `GateState=BlockedUntilExplicitM7Authorization`。
- 当前只允许 planning 和 readiness，不允许 execution。
