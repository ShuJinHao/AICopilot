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
- Cloud AiRead 行数唯一 owner、typed query/DTO：`src/services/AICopilot.Services.Contracts/Contracts/CloudAiReadContracts.cs`。
- Cloud provider item/envelope 运行时契约：`CloudAiReadProviderItemContractValidator.cs`、`CloudAiReadJsonValueReader.cs`。
- 语义分析执行：`src/services/AICopilot.AiGatewayService/Workflows/Executors/SemanticAnalysisRunner.cs`。
- CloudReadOnly Text-to-SQL：`src/services/AICopilot.AiGatewayService/Workflows/Executors/CloudReadOnly*`、`src/services/AICopilot.Services.Contracts/Contracts/CloudReadOnlyTextToSql*`。
- governed schema 和 SQL guard：`src/infrastructure/AICopilot.Dapper`、`src/services/AICopilot.Services.CrossCutting/Sql`。
- Cloud readonly 授权脚本：`deploy/enterprise-ai/cloud-readonly/apply-readonly-grants.sql`、`deploy/enterprise-ai/cloud-readonly/check-readonly-grants.sql`。
- Cloud readonly 授权 preflight：`deploy/enterprise-ai/scripts/apply-cloud-readonly-grants.sh`、`deploy/enterprise-ai/scripts/check-cloud-readonly-grants.sh`。
- 关键测试：`AICopilotArchitectureAnalyzerTests` 的 `AIARCH006/AIARCH007`、`ArchitectureBoundaryTests`、`CloudReadonlyGrantSql_ShouldMatchGovernedRuntimeTables`、`CloudAiReadClientContractTests`、`SemanticDefinitionTests`、`CloudReadonlySimulationTests`、`CloudReadOnlyTextToSqlFallbackRunnerTests`、`ToolSafetyAndApprovalIdentityTests`、`TextToSqlReadOnlyTests`、`DataAnalysisFinalContextFormatterTests`、`AgentSafetyApplicationTests`、`PromptGovernanceTests`、`SemanticAnalysisRunnerTests`、`DeviceLogFollowUpIntentRewriterTests`。

### 编译型只读门禁

- `AIARCH006` 以 Roslyn symbol/operation 对所有源码方法（包括 internal/private/protected HostedService 路径）逐个判断 Cloud root：该方法自身直接调用、构造或泛型解析完全限定真实 Cloud AiRead / CloudReadOnly operation，签名/字段/ctor 持有正式 client，或自身属于正式 fallback/provider/tool workflow 时才命中；随后从该 root 追踪完整 call graph、具体实现、interface dispatch、泛型 helper、lambda 以及 field/property delegate。Delegate member initializer、constructor assignment 和 property getter return 必须在 CompilationEnd 统一解析，不能受并发 Analyzer callback 顺序影响。中性命名方法通过本地 DI factory、private helper 返回或 object creation 取得真实 Cloud read 类型时同样命中。Generic Agent/worker orchestrator 不能仅因深层 interface dispatch 某个实现可能读 Cloud 就被整体误标；可信 operation 只来自契约记录的完全限定 client、read-only interface/provider/tool executor 和正式 fallback workflow，同名伪 Cloud 类型、仅计划/DTO 类型或方法名不能扩大入口。
- Cloud root 可达图中的写边矩阵固定为：完全限定 `AICopilot.SharedKernel.Repository.IRepository/IReadRepository` mutation；`SaveChanges*`、`ExecuteNonQuery*`、EF raw/bulk write；完全限定 `Dapper.SqlMapper.Execute/ExecuteAsync`；参数实现完全限定 `AICopilot.SharedKernel.Messaging.ICommand` 的 dispatch；以及完全限定 `AICopilot.AiGatewayService.AgentTasks.IAgentToolExecutor/McpAgentToolExecutor.ExecuteAsync`。这些边全部是 compiler error，只允许下条列出的两个 AICopilot 内部持久化契约按完全限定 symbol identity 截断。同名 `Fixture.IRepository`、`Fixture.ICommand`、`Fixture.SqlMapper`、含 `Mcp`/`Write` 字样的方法或 executor 均不得触发或扩大规则；不得以 SQL 字符串关键词代替 symbol identity。
- 只读路径只允许两个精确的 AICopilot 内部持久化例外：`AICopilot.Services.Contracts.IAuditLogWriter` 只能记录只读查询审计；`AICopilot.Services.Contracts.IModelQuotaReservationStore` 只能执行 `TryReserveAsync`、`SettleAsync`、`ReclaimExpiredAsync`，维护模型调用前后的租约、限流、并发和 token 配额状态。配额接口唯一生产实现必须是 `AICopilot.EntityFrameworkCore.Repository.PostgresModelQuotaReservationStore`，只能依赖 `AgentExecutionTransactionRunner`，且该 runner 的唯一 DbContext 必须是 `AiGatewayDbContext`。两者都不是 Cloud 业务写权限；同名接口、其他实现、adapter/wrapper、其他 DbContext、实现类额外写方法或借内部状态写入执行 Cloud mutation 均不在例外内。
- `AIARCH007` 只接受完全限定符号上的 CloudReadOnly tool safety descriptor，且安全元数据必须同时为 `boundary=CloudReadOnly`、`capability=ReadOnlyQuery`、`readOnlyDeclared=true`；`Diagnostics`、`LocalSuggestion`、`SideEffecting`、缺失值、同名伪类型或其他无法静态证明的动态声明都必须 compiler-error fail-closed。动态 MCP 配置不能因 Analyzer 无法展开就绕过安全契约；注册和每次执行都必须通过同一 `AiToolSafetyPolicy.EvaluateConfigured` 运行时门禁。

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

`Device`、`DeviceLog`、`Capacity`、`ProductionData`、`Process`、`ClientRelease` 六类已覆盖正式 `Analysis.*` 语义必须只走上述 Cloud AiRead typed GET。Cloud 成功、合法空集、关闭、语义规划失败、400、401、403、429、5xx、timeout 或非法 JSON 都不得回退 Direct DB、Text-to-SQL、Simulation、MCP 或隐藏适配器。

`Analysis.Recipe.*` 具体数据问题必须在语义规划器、数据提供方、数据库、SQL 生成器和 fallback 之前返回固定禁读边界；规划器失败也不得改变该结论。`SemanticAnalysisRunner` 的运行时依赖只允许 Cloud AiRead 客户端、语义规划器和日志器，不得重新注入 Direct DB、physical mapping、SQL generator、数据库审计或 Text-to-SQL fallback。

既有 physical mapping / semantic source status 属于 Direct DB 治理和运维诊断表面，不是正式语义执行授权；其配置、状态 API 或独立测试存在，不得被解释为六类 Cloud-only intent 可以转入 Direct DB。

`production-records` 是生产记录高频读取的唯一 Cloud AiRead 路径。不得新增旁路 endpoint、MCP 写工具或直接 SQL 高优先路径替代它。

生产记录字段必须保持来源真实性：当前 Cloud 正式记录提供 `typeKey/typeName/deviceId/deviceName` 等字段，不提供 `processName/stationName/deviceCode`。缺失字段必须保持不存在或空；不得用 `typeName`、`typeKey` 或其他显示字段代填、推断工序、工位或设备编码。

### 3.1 行数唯一 owner 与 sealed plan 边界

- Cloud AiRead 的唯一行数策略是 `CloudAiReadRowLimitPolicy`：`MinimumRows=1`、`MaxRows=100`。Cloud 六类 semantic definition、八个 typed GET、query parameter、result limit 和 Simulation/Real provider guard 都必须引用这一 owner；不得另设 `200`、配置型上限或 endpoint 私有上限。
- 尚未 sealed 的 planner 输入必须在构造 `SemanticQueryPlan` 前 normalize 到 `1..100`；直接 typed `CloudAiReadQuery` 在 client ingress、构造 query parameter 和发出 HTTP 前 normalize 到 `1..100`。
- 已形成的 `SemanticQueryPlan` 是 frozen typed contract，不得为“继续执行”而 clamp 或改写。`Limit < 1` 或 `Limit > 100` 必须在 provider resolver/client/HTTP 前以 `AppProblemCodes.CloudReadonlyIntentUnsupported` 和固定消息 `Cloud readonly intent violates the frozen typed semantic plan contract.` 拒绝；合法 limit 必须原值保留。
- AICopilot 中其它明确属于非 Cloud 的通用预算即使使用不同上限，也不得被解释为 Cloud AiRead row limit；命中 Cloud semantic intent、Cloud node input 或 Cloud provider 时只能使用 `CloudAiReadRowLimitPolicy`。

### 3.2 八个 GET 的 exact provider item schema

以下 JSON 名称和类型是 Cloud provider 与 AICopilot consumer 的跨项目契约，不是 AI 端宽松解析提示。每个 `items[]` 元素必须是 object，字段名按 exact camelCase 匹配；缺字段、未知字段、同名重复字段、仅大小写不同的碰撞、错误 JSON kind、空 Guid、越界整数或不可解析日期时间都必须以 `CloudAiReadProblemCodes.Unavailable` 和固定消息 `Cloud AiRead endpoint returned an invalid provider contract.` 拒绝。

| GET | `items[]` exact schema |
| --- | --- |
| `/devices` | `id: Guid`、`deviceCode: string`、`deviceName: string`、`processId: Guid` |
| `/processes` | `id: Guid`、`processCode: string`、`processName: string` |
| `/client-releases` | `id: Guid`、`componentKind: string`、`componentKey: string`、`displayName: string`、`channel: string`、`targetRuntime: string`、`version: string`、`status: string`、`releaseNotes: string?`、`createdAtUtc: DateTime`、`publishedAtUtc: DateTime?`、`deletedAtUtc: DateTime?` |
| `/device-client-states` | `deviceId: Guid`、`deviceName: string`、`clientCode: string`、`primaryIp: string?`、`channel: string?`、`hostVersion: string?`、`hostApiVersion: string?`、`versionReportedAtUtc: DateTime?`、`versionReceivedAtUtc: DateTime?`、`softwareStatus: string`、`runtimeStatus: string?`、`runtimeStartedAtUtc: DateTime?`、`lastRuntimeHeartbeatAtUtc: DateTime?`、`updatedAtUtc: DateTime?` |
| `/capacity/summary` | `date: DateOnly(yyyy-MM-dd)`、`totalCount: int32`、`okCount: int32`、`ngCount: int32`、`dayShiftTotal: int32`、`nightShiftTotal: int32` |
| `/capacity/hourly` | `time: DateTime`、`date: DateOnly(yyyy-MM-dd)`、`hour: int32`、`minute: int32`、`timeLabel: string`、`shiftCode: string`、`totalCount: int32`、`okCount: int32`、`ngCount: int32`、`okRate: decimal` |
| `/device-logs` | `id: Guid`、`deviceId: Guid`、`deviceName: string`、`level: string`、`message: string`、`logTime: DateTime`、`receivedAt: DateTime` |
| `/production-records` | `recordId: Guid`、`typeKey: string`、`typeName: string`、`deviceId: Guid`、`deviceName: string`、`barcode: string?`、`result: string?`、`completedAt: DateTime?`、`receivedAt: DateTime?`、`fields: scalar object`、`fieldSchema: exact object[]` |

表中的 `?` 表示“key 必须存在、value 可以为 JSON null”，不表示可省略 key。该 required-present nullable 规则覆盖 client release 的 `releaseNotes/publishedAtUtc/deletedAtUtc`、device state 的全部 nullable 字段、production record 的 `barcode/result/completedAt/receivedAt`，以及每个 `fieldSchema` entry 的 `unit/precision`。固定 item 的 `DateTime/DateTime?` 与 Cloud DTO 对齐；只有 envelope `asOfUtc` 使用 `DateTimeOffset`。

### 3.3 Envelope 与全量验证顺序

- provider envelope 必须包含 `items: array`、可解析为 `DateTimeOffset` 的 `asOfUtc: string`、非空 `source: string`、`queryScope: string`、`rowCount: int32 >= 0`、`truncated: bool` 和 required-present 的 `nextCursor: string|null`。
- `rowCount` 必须等于原始 `items` 数组长度，不得等于 client `Take(limit)` 后的长度，也不得用 metadata 掩盖少行或多行。
- consumer 必须先 clone 并验证所有原始 item，再应用 normalized limit 的 `Take`。请求 `limit=1` 时第二条或更后面的 malformed item 仍必须让整个 response fail-closed；不得通过先截断来隐藏 provider drift。

### 3.4 Production `fields/fieldSchema` 跨契约

- 每个 `fieldSchema[]` entry exact schema 为 `key: string`、`label: string`、`type: string`、required-present `unit: string|null`、required-present `precision: int32|null`、`required: bool`；entry 的缺失、未知、重复或大小写碰撞字段均拒绝。
- schema key 必须匹配 `[a-z][a-zA-Z0-9]*`，并在 exact 与大小写不敏感口径下都唯一；点号、下划线、首字母大写、空 key 和 case collision 均拒绝。
- `type` 只允许 `string`、`number`、`integer`、`boolean`、`datetime`、`enum` 六种 exact 值。`fields` 只能包含 string、JSON number、bool 或 null 标量，字段名必须 exact 命中一条 schema；未知字段、object/array value、重复字段和大小写碰撞均拒绝。
- `string/enum` 使用 JSON string，且 `required=true` 时不能空白；`datetime` 使用可由 `DateTime` 解析的 JSON string；`number` 必须可表示为 `decimal`；`integer` 必须是可表示为 `decimal` 且无小数部分的 JSON number，因此允许超过 `Int64` 但仍在 `decimal` 范围内的整数；`boolean` 只接受 JSON bool。
- `required` 的当前 Cloud 稀疏记录语义是：字段出现在 `fields` 时，`required=true` 不允许 null；`required=false` 才允许 null。`fieldSchema` 可以包含当前 `fields` 未返回的字段，即使该 schema entry 为 required；consumer 不得擅自把“当前行未携带”改判为 provider 违约，也不得给缺失值补默认数据。
- Cloud 端 provider DTO、upload/validator、AiRead query service 与 AI 端 DTO/validator/tests 共同拥有这份跨契约。任何一侧改字段、nullable、类型、safe-key、row limit 或 envelope，必须按 Cloud provider tests → AI consumer tests → clean-HEAD 原字节 digest → 非生产 live 联合验收顺序重做证据；单侧 fixture 或同名快照不能替代真实双方绑定。

## 4. 参数和身份

- Cloud AiRead 正式设备参数是 `deviceId`。
- `deviceCode` / `ClientCode` 只能用于设备查询、解析或展示，不能当 `deviceId` 发给业务读取端点。
- `/devices` 支持 `deviceId/deviceCode/processId/keyword/maxRows`，多个条件按 AND 相交，返回设备主数据 `id/deviceCode/deviceName/processId`；不得从该端点读取或生成运行状态、日志级别、`lineName`、`processName` 或 `updatedAt`。
- 自然语言里的设备编码必须先解析成唯一 `deviceId`；只有未截断搜索结果中的唯一精确规范化 `deviceCode` 匹配可以用于解析。零个或多个精确匹配、结果截断或只有模糊命中时要求用户补充正式 `deviceId`，不得扫描分页或选择第一条。
- `Analysis.Device.Status` 只调用 `/device-client-states`，以 `softwareStatus` 为 Cloud 权威派生状态，`runtimeStatus` 保留心跳原值，`lastRuntimeHeartbeatAtUtc` 是唯一 freshness 时间。无心跳设备必须返回 `MissingRuntimeHeartbeat` 行；仅 `asOfUtc - lastRuntimeHeartbeatAtUtc > 24h` 为 `RuntimeHeartbeatStale`，恰好 24 小时不 stale；Stale 不得翻译为 Offline/Stopped。零条只表示授权范围内没有匹配设备。
- `Analysis.Device.Status` 不得回退 Direct DB、Text-to-SQL 或 Simulation；Direct DB 设备主数据映射不得连接 `device_logs`，最新日志级别只属于 `Analysis.DeviceLog.*`。
- `Analysis.Process.List` 只调用 `/processes`，支持正式 `processId/keyword/maxRows`；`processCode/processName` 作为搜索语义规范化为 keyword。`Analysis.Process.Detail` 必须至少携带 `processId/processCode/processName` 之一，`keyword` 只能用于 List，keyword-only Detail 必须在 sealed plan/provider/HTTP 前拒绝。`processId` 必须作为 GUID 精确参数发送，并且直查响应只能有一条、不得截断且返回 `processId` 必须与请求完全一致；非 `processId` 搜索分支必须先形成非空 `processCode/processName` exact filters，再在未截断结果中唯一精确命中。空 exact filters、只有模糊命中、零命中、多命中或截断都必须返回明确边界，不得让 `All(empty)` 变成成功、猜测或选择第一条。
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
- DataAnalysis 最终上下文是独立的不可信消费边界：表结构注释、Text-to-SQL alias 和返回行 key 均不能直接成为最终 JSON 属性名。formatter 必须使用唯一字段标签映射，过滤共享 governed-schema 敏感标识，并让 metadata name/description 与 preview key 保持一致。
- `business_data_preview` 只是扁平业务标量预览；除 string、bool/数值、date、Guid、enum 与等价 JSON 标量外，其余 object/array/collection 不得递归输出、串行化或调用自定义 `ToString()` 透出 nested key/secret，统一用既有脱敏占位表达不可展开值。

## 8. Simulation 边界

- Simulation 只允许作为显式 Development、离线演示或测试资产。
- `appsettings.json` 与 `appsettings.Development.json` 叠加后的默认值都必须保持 `CloudReadonly.Mode=Disabled`、`Simulation.Enabled=false` 和 `CloudAiRead.Enabled=false`；Development 的 Simulation 只能由专用测试 fixture、显式环境变量或显式启动参数逐次开启，不能依赖开发配置文件自动开启。
- 生产基础配置、compose 和部署模板不得携带 `MockOnly=true` 或默认 Simulation 开关。
- Real Cloud 查询失败、为空或未配置时，必须返回 Cloud AiRead / CloudReadOnly 错误或空态，不能降级为 Simulation。
- 任何产物、报告、artifact 或导出里出现 Simulation 数据时，必须带明确 `sourceMode=Simulation`、`isSimulation=true` 或等价来源标记。
- Simulation acceptance 只使用 Manual-only 固定 Linux runner：先以 `docker info` 验证 Linux Docker daemon，再分别整项目执行 `AICopilot.SimulationTests` 和 `AICopilot.SimulationDockerTests`。缺 Docker 必须失败，不得 Skip，不得用 `--filter`、Suite/Phase/Batch/类名或静态 changed-files 清单缩小 acceptance。
- Simulation pure/Docker runner 必须分别产生 TRX 并对账 12/1；报告、JSON 摘要和 TRX 只能写入已 ignore 的 `artifacts/simulation/`，Manual workflow 必须以 `always()` 上传该 evidence，不得写入 `docs/`、`资料/` 或新增单任务验收文档。

## 9. 验收命令

以下命令用于 Cloud 只读专题定向诊断；任务完成仍必须对账 inventory 中全部 required runner、Web 和 deployment behavior，不得用 filter 结果代替。

```bash
dotnet test src/tests/AICopilot.Architecture.AnalyzerTests/AICopilot.Architecture.AnalyzerTests.csproj --filter "AIARCH006|AIARCH007_ShouldRequireControllerMetadataAndCloudReadOnlySafetyMetadata" --no-restore
dotnet test src/tests/AICopilot.DeploymentTests/AICopilot.DeploymentTests.csproj --filter "CloudReadonlyGrantSql_ShouldMatchGovernedRuntimeTables" --no-restore
dotnet test src/tests/AICopilot.InProcessTests/AICopilot.InProcessTests.csproj --filter "CloudAiReadClientContractTests" --no-restore
dotnet test src/tests/AICopilot.ApplicationTests/AICopilot.ApplicationTests.csproj --filter "SemanticDefinitionTests" --no-restore
dotnet test src/tests/AICopilot.ContractTests/AICopilot.ContractTests.csproj --filter "CloudReadonlyChatBoundaryTests" --no-restore
dotnet test src/tests/AICopilot.UnitTests/AICopilot.UnitTests.csproj --filter "CloudReadOnly|PromptGovernanceTests|DeviceLogFollowUpIntentRewriterTests" --no-restore
dotnet test src/tests/AICopilot.ApplicationTests/AICopilot.ApplicationTests.csproj --filter "TextToSqlReadOnlyTests|SemanticAnalysisRunnerTests|AgentSafetyApplicationTests|ToolSafetyAndApprovalIdentityTests.CloudReadOnlyToolSafety_ShouldRejectForbiddenWriteVerbs" --no-restore
dotnet test src/tests/AICopilot.GoldenEvalTests/AICopilot.GoldenEvalTests.csproj --no-restore
dotnet test src/tests/AICopilot.SimulationTests/AICopilot.SimulationTests.csproj -c Release --no-build --no-restore
docker info --format '{{.OSType}}'
dotnet test src/tests/AICopilot.SimulationDockerTests/AICopilot.SimulationDockerTests.csproj -c Release --no-build --no-restore
dotnet test src/tests/AICopilot.CloudAiReadLiveTests/AICopilot.CloudAiReadLiveTests.csproj -c Release --no-build --no-restore
rg -n "CloudAiRead|CloudReadOnly|production-records|PreviousSqlForRepair|CloudReadOnlyGovernedSchema|Simulation|MockOnly" src deploy docs
```

`AICopilot.CloudAiReadLiveTests` 只用于显式跨仓联合验收：必须从环境变量读取当前非生产 Cloud BaseUrl/token 和测试实体标识，不允许 StubHandler、手写 JSON 或 Simulation 充当 provider；缺任一变量必须失败，不能 Skip。token 只允许由 Cloud 隔离 E2E 宿主经子进程环境传递，不得进入参数、日志、summary 或仓库。任一仓库生产源码变化后旧 live 结果立即失效。

## 10. 外部依赖

- CloudPlatform 端 AiRead API 的具体实现、权限、nginx/OIDC Provider 部署口径属于 Cloud 项目。
- Cloud PostgreSQL 只读账号创建、授权 SQL 执行和真实 grant 检查需要真实 Cloud 数据库和运维窗口。
- AICopilot 文档和测试不能伪造 Cloud 端 endpoint 已发布、真实数据库已授权或生产查询已通过。
