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

## 3. Cloud AiRead 正式唯一路径

Cloud 当前正式 AI Read 只读表面必须在 AICopilot 客户端 allowlist 中逐项对齐：

- 设备：`/api/v1/ai/read/devices`
- 工序：`/api/v1/ai/read/processes`
- 客户端发布版本：`/api/v1/ai/read/client-releases`
- 设备客户端状态：`/api/v1/ai/read/device-client-states`
- 汇总产能：`/api/v1/ai/read/capacity/summary`
- 小时产能：`/api/v1/ai/read/capacity/hourly`
- 设备日志：`/api/v1/ai/read/device-logs`
- 生产记录：`/api/v1/ai/read/production-records`

Cloud AiRead transport 只允许以上八个固定 GET。AICopilot 不提供任意 method/path 公共传输入口，不接受可配置 POST allowlist；POST、PUT、PATCH、DELETE 必须在发出 HTTP 请求前拒绝。Cloud identity status 是独立的只读身份 GET 表面，只复用安全路径校验，不扩展 Cloud AiRead 业务端点。

`Device`、`DeviceLog`、`Capacity`、`ProductionData`、`Process`、`ClientRelease` 六类已覆盖正式 `Analysis.*` 语义必须只走上述 Cloud AiRead typed GET。Cloud 成功、合法空集、关闭、400、401、403、429、5xx、timeout 或非法 JSON 都不得回退 Direct DB、Text-to-SQL、Simulation、MCP 或隐藏适配器。

`production-records` 是生产记录高频读取的唯一 Cloud AiRead 路径。不得新增旁路 endpoint、MCP 写工具或直接 SQL 高优先路径替代它。

生产记录字段必须保持来源真实性：当前 Cloud 正式记录提供 `typeKey/typeName/deviceId/deviceName` 等字段，不提供 `processName/stationName/deviceCode`。缺失字段必须保持不存在或空；不得用 `typeName`、`typeKey` 或其他显示字段代填、推断工序、工位或设备编码。

## 4. 参数和身份

- Cloud AiRead 正式设备参数是 `deviceId`。
- `deviceCode` / `ClientCode` 只能用于设备查询、解析或展示，不能当 `deviceId` 发给业务读取端点。
- `/devices` 支持 `deviceId/deviceCode/processId/keyword/maxRows`，多个条件按 AND 相交，返回设备主数据 `id/deviceCode/deviceName/processId`；不得从该端点读取或生成运行状态、日志级别、`lineName`、`processName` 或 `updatedAt`。
- 自然语言里的设备编码必须先解析成唯一 `deviceId`；只有未截断搜索结果中的唯一精确规范化 `deviceCode` 匹配可以用于解析。零个或多个精确匹配、结果截断或只有模糊命中时要求用户补充正式 `deviceId`，不得扫描分页或选择第一条。
- `Analysis.Device.Status` 只调用 `/device-client-states`，以 `softwareStatus` 为 Cloud 权威派生状态，`runtimeStatus` 保留心跳原值，`lastRuntimeHeartbeatAtUtc` 是唯一 freshness 时间。无心跳设备必须返回 `MissingRuntimeHeartbeat` 行；仅 `asOfUtc - lastRuntimeHeartbeatAtUtc > 24h` 为 `RuntimeHeartbeatStale`，恰好 24 小时不 stale；Stale 不得翻译为 Offline/Stopped。零条只表示授权范围内没有匹配设备。
- `Analysis.Device.Status` 不得回退 Direct DB、Text-to-SQL 或 Simulation；Direct DB 设备主数据映射不得连接 `device_logs`，最新日志级别只属于 `Analysis.DeviceLog.*`。
- `Analysis.Process.List` 只调用 `/processes`，支持正式 `processId/keyword/maxRows`；`processCode/processName` 作为搜索语义规范化为 keyword。`Analysis.Process.Detail` 的 `processId` 必须作为 GUID 精确参数发送，按编码或名称搜索时必须在未截断结果中唯一精确命中；零命中、多命中或截断必须返回明确边界，不得猜测或选择第一条。
- `Analysis.ClientRelease.List` 只调用 `/client-releases`，只允许 `channel/targetRuntime/status/includeArchived`。版本、hash、下载地址、发布说明、归档和发布状态只能逐字段使用 Cloud 返回，不能由模型推断、拼接或补默认值。
- `Analysis.Device.List/Detail/Status`、`Analysis.DeviceLog.Latest/Range/ByLevel`、`Analysis.Capacity.Range/ByDevice`、`Analysis.ProductionData.Latest/Range/ByDevice`、`Analysis.Process.List/Detail` 与 `Analysis.ClientRelease.List` 都是 Cloud-only 能力；`Analysis.Capacity.ByProcess` 在正式聚合契约形成前不路由。Cloud AiRead 关闭、空集、拒绝、返回模糊结果或失败时，不得回退其它数据源。
- `scenarioId`、`from`、`to`、`pilotWindowId`、`boundary` 等 AICopilot 内部试点/执行元数据不得透传 Cloud。
- Cloud 只读请求只能发送 Cloud 端点真实声明的参数。

## 5. Direct DB 和 Text-to-SQL

CloudReadOnly Direct DB 只能服务已覆盖正式语义之外的低频自由探索和治理白名单内补充分析；Text-to-SQL 只能在这类未覆盖问题或其 Direct DB SQL 失败后受控 fallback。两者均不得执行、拆分、重命名或旁路上述 Cloud-only `Analysis.*` intent。它们必须同时满足：

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
- `appsettings.json` 与 `appsettings.Development.json` 叠加后的默认值都必须保持 `CloudReadonly.Mode=Disabled`、`Simulation.Enabled=false` 和 `CloudAiRead.Enabled=false`；Development 的 Simulation 只能由专用测试 fixture、显式环境变量或显式启动参数逐次开启，不能依赖开发配置文件自动开启。
- 生产基础配置、compose 和部署模板不得携带 `MockOnly=true` 或默认 Simulation 开关。
- Real Cloud 查询失败、为空或未配置时，必须返回 Cloud AiRead / CloudReadOnly 错误或空态，不能降级为 Simulation。
- 任何产物、报告、artifact 或导出里出现 Simulation 数据时，必须带明确 `sourceMode=Simulation`、`isSimulation=true` 或等价来源标记。

## 9. 验收命令

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --filter "CloudReadOnly|TextToSql|CloudWrite" --no-restore
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "CloudAiReadClientTests|CloudReadonlySimulationTests|CloudReadOnlyTextToSqlFallbackRunnerTests|TextToSqlReadOnlyTests|AiEvalBehaviorGuardrailTests|PromptGovernanceTests|SemanticAnalysisRunnerTests|DeviceLogFollowUpIntentRewriterTests" --no-restore
dotnet test src/tests/AICopilot.CloudAiReadLiveTests/AICopilot.CloudAiReadLiveTests.csproj -c Release --no-build --no-restore
rg -n "CloudAiRead|CloudReadOnly|production-records|PreviousSqlForRepair|CloudReadOnlyGovernedSchema|Simulation|MockOnly" src deploy docs
```

`AICopilot.CloudAiReadLiveTests` 只用于显式跨仓联合验收：必须从环境变量读取当前非生产 Cloud BaseUrl/token 和测试实体标识，不允许 StubHandler、手写 JSON 或 Simulation 充当 provider；缺任一变量必须失败，不能 Skip。token 只允许由 Cloud 隔离 E2E 宿主经子进程环境传递，不得进入参数、日志、summary 或仓库。任一仓库生产源码变化后旧 live 结果立即失效。

## 10. 外部依赖

- CloudPlatform 端 AiRead API 的具体实现、权限、nginx/OIDC Provider 部署口径属于 Cloud 项目。
- Cloud PostgreSQL 只读账号创建、授权 SQL 执行和真实 grant 检查需要真实 Cloud 数据库和运维窗口。
- AICopilot 文档和测试不能伪造 Cloud 端 endpoint 已发布、真实数据库已授权或生产查询已通过。
