# Cloud 只读数据分析契约

本文档约束 AICopilot 读取 Cloud 业务数据、Cloud AiRead、CloudReadOnly Direct DB 和 Text-to-SQL 的边界。总计划见 `docs/AI架构治理清单.md`。

## 1. 总边界

- AICopilot 只能读取已批准范围内的 Cloud 业务数据，用于分析、解释、汇总、检索和建议。
- AICopilot 不得创建、修改、删除、补录、审批、派发或触发 Cloud 业务流程。
- AICopilot 不得通过 MCP、Tool、Agent workflow、后台任务、直接 SQL 或隐藏 adapter 间接写 Cloud。
- Human-in-the-loop 只控制 AICopilot 自身高风险动作，不授权 Cloud 业务写入。
- Cloud 只读失败、为空或未配置时，不得 fallback 到 Simulation 冒充真实数据。

## 2. 源码归属

- Cloud AiRead transport 和 endpoint policy：`src/infrastructure/AICopilot.Infrastructure/CloudRead`。
- 语义分析执行：`src/services/AICopilot.AiGatewayService/Workflows/Executors/SemanticAnalysisRunner.cs`。
- CloudReadOnly Text-to-SQL：`src/services/AICopilot.AiGatewayService/Workflows/Executors/CloudReadOnly*`、`src/services/AICopilot.Services.Contracts/Contracts/CloudReadOnlyTextToSql*`。
- governed schema 和 SQL guard：`src/infrastructure/AICopilot.Dapper`、`src/services/AICopilot.Services.CrossCutting/Sql`。
- Cloud readonly 授权脚本：`deploy/enterprise-ai/cloud-readonly/apply-readonly-grants.sql`、`deploy/enterprise-ai/cloud-readonly/check-readonly-grants.sql`。
- Cloud readonly 授权 preflight：`deploy/enterprise-ai/scripts/apply-cloud-readonly-grants.sh`、`deploy/enterprise-ai/scripts/check-cloud-readonly-grants.sh`。
- 关键测试：`ArchitectureBoundaryTests`、`CloudAiReadClientTests`、`CloudReadonlySimulationTests`、`CloudReadOnlyTextToSqlFallbackRunnerTests`、`TextToSqlReadOnlyTests`、`AiEvalBehaviorGuardrailTests`、`PromptGovernanceTests`、`SemanticAnalysisRunnerTests`、`DeviceLogFollowUpIntentRewriterTests`。

## 3. Cloud AiRead 优先路径

高频业务读取必须优先走 Cloud AiRead 正式只读 API：

- 设备日志：`/api/v1/ai/read/device-logs`
- 汇总产能：`/api/v1/ai/read/capacity/summary`
- 小时产能：`/api/v1/ai/read/capacity/hourly`
- 生产记录：`/api/v1/ai/read/production-records`

`production-records` 是生产记录高频读取的唯一 Cloud AiRead 路径。不得新增旁路 endpoint、MCP 写工具或直接 SQL 高优先路径替代它。

## 4. 参数和身份

- Cloud AiRead 正式设备参数是 `deviceId`。
- `deviceCode` / `ClientCode` 只能用于设备查询、解析或展示，不能当 `deviceId` 发给业务读取端点。
- 自然语言里的设备编码必须先解析成唯一 `deviceId`；无法唯一命中时要求用户补充。
- `scenarioId`、`from`、`to`、`pilotWindowId`、`boundary` 等 AICopilot 内部试点/执行元数据不得透传 Cloud。
- Cloud 只读请求只能发送 Cloud 端点真实声明的参数。

## 5. Direct DB 和 Text-to-SQL

CloudReadOnly Direct DB 只能作为低频探索、治理白名单内补充分析或 Cloud AiRead 未覆盖链路的受控 fallback。它必须同时满足：

- 使用已验证的只读 PostgreSQL 账号。
- 表级 `GRANT SELECT` 只覆盖治理白名单表，不使用 `GRANT SELECT ON ALL TABLES`、默认权限、未来表自动授权或写权限。
- SQL 必须经过 SELECT-only guard、governed schema、BusinessQuery safety policy 双层表/列白名单和只读执行。
- 生产启用 Direct DB 前必须执行 readonly grant preflight。
- 权限错误只能暴露治理白名单内表名和只读权限不足结论，不能输出连接串、role、密码、SQL 原文或非白名单对象。

Text-to-SQL prompt 只能暴露 `CloudReadOnlyGovernedSchema` 批准的表名、列名、列类型、join hints 和必要业务描述。不得暴露：

- 连接串、凭据、role/权限细节。
- 样例数据、查询结果、参数值。
- 非白名单表字段、系统字段或敏感字段。
- 用户 prompt 原文、SQL 原文、连接串或 endpoint。

## 6. 修复重试和审计

- Text-to-SQL 修复重试默认最多 3 次，硬上限 5 次。
- timeout、权限、凭据、非只读、系统表、敏感字段、多语句或写 SQL 默认不可修复、不重试。
- 上一轮失败 SQL 只允许作为当前调用内存参数 `PreviousSqlForRepair` 临时回传给 LLM。
- `PreviousSqlForRepair` 不得进入审计、日志、state、结果、DTO 或持久化对象。
- 成功/失败结果只保存 hash、长度、行数、截断状态、失败分类和安全摘要。

## 7. DeviceLog 和最终回答

- DeviceLog 日志级别必须使用 Cloud PostgreSQL 真实枚举 `ERROR`、`WARN`、`INFO`。
- “错误+警告”“异常分析”等场景必须显式查询多级别，不能只查 `ERROR` 后推断 `WARN` 没有。
- 追问其他日志级别、设备、工序或时间窗口时，必须重新生成并执行本轮 `Analysis.DeviceLog.*` 查询。
- 最终回答只能总结本轮 `query_execution`、`semantic_summary`、返回行数、过滤条件和证据边界；不能基于上一轮回答文本推断未查询数据。
- Widget 和 display blocks 只能重排本轮只读查询事实，不能前端编造指标、Markdown 解析补数据或把建议写成已执行动作。

## 8. Simulation 边界

- Simulation 只允许作为显式 Development、离线演示或测试资产。
- 生产基础 `appsettings.json`、compose 和部署模板不得携带 `MockOnly=true` 或默认 Simulation 开关。
- Real Cloud 查询失败、为空或未配置时，必须返回 Cloud AiRead / CloudReadOnly 错误或空态，不能降级为 Simulation。
- 任何产物、报告、artifact 或导出里出现 Simulation 数据时，必须带明确 `sourceMode=Simulation`、`isSimulation=true` 或等价来源标记。

## 9. 验收命令

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --filter "CloudReadOnly|TextToSql|CloudWrite" --no-restore
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "CloudAiReadClientTests|CloudReadonlySimulationTests|CloudReadOnlyTextToSqlFallbackRunnerTests|TextToSqlReadOnlyTests|AiEvalBehaviorGuardrailTests|PromptGovernanceTests|SemanticAnalysisRunnerTests|DeviceLogFollowUpIntentRewriterTests" --no-restore
rg -n "CloudAiRead|CloudReadOnly|production-records|PreviousSqlForRepair|CloudReadOnlyGovernedSchema|Simulation|MockOnly" src deploy docs
```

## 10. 外部依赖

- CloudPlatform 端 AiRead API 的具体实现、权限、nginx/OIDC Provider 部署口径属于 Cloud 项目。
- Cloud PostgreSQL 只读账号创建、授权 SQL 执行和真实 grant 检查需要真实 Cloud 数据库和运维窗口。
- AICopilot 文档和测试不能伪造 Cloud 端 endpoint 已发布、真实数据库已授权或生产查询已通过。
