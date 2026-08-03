# Cloud 只读数据分析契约

本文档是 AICopilot 业务查询 typed-first、结构化结果矩阵、查询确认、受控 Text-to-SQL、Simulation 边界和 fallback 决策的唯一技术正文，并约束统一业务数据源插件、当前 Cloud 读取与共享 SQL 安全边界。其它活动规则只保留产品/安全摘要并链接本文，不复制 policy、状态矩阵、重试或治理实现细节。

## 1. 总边界

- AICopilot 只能读取已批准范围内的 Cloud 业务数据，用于分析、解释、汇总、检索和建议。
- AICopilot 不得创建、修改、删除、补录、审批、派发或触发 Cloud 业务流程。
- AICopilot 不得通过 MCP、Tool、Harness、后台任务、直接 SQL 或隐藏 adapter 间接写 Cloud。
- Human-in-the-loop 只控制 AICopilot 自身高风险动作，不授权 Cloud 业务写入。
- Cloud 只读失败、为空或未配置时，不得 fallback 到 Simulation 冒充真实数据。
- 当前唯一真实外部业务数据源是 Cloud。MES、ERP 只允许以后通过统一 provider/profile registry 扩展，不得复制 Runner、Guard、RepairLoop 或 Prompt。
- 分析任务必须先确认来源、`Device|DeviceLog|Capacity|ProductionRecord|Process|ClientRelease` 数据类型、业务对象、时间范围和过滤条件；信息不足、来源不唯一或低置信度时先询问。同一 `SessionId` 内只按已确认 scope 复用上下文，时间、过滤或来源等 scope 变化时继续按本文查询确认规则处理。

## 2. 源码归属

- Cloud AiRead transport 和 endpoint policy：`src/infrastructure/AICopilot.Infrastructure/CloudRead`。
- Cloud AiRead 行数唯一 owner、typed query/DTO：`src/services/AICopilot.Services.Contracts/Contracts/CloudAiReadContracts.cs`。
- Cloud provider item/envelope 运行时契约：`CloudAiReadProviderItemContractValidator.cs`、`CloudAiReadJsonValueReader.cs`。
- 统一上下文、确认字段、领域能力、结构化结果、provider/profile/context 接口：`src/services/AICopilot.Services.Contracts/Contracts/BusinessQueryPipelineContracts.cs`。
- profile registry 与上下文 TTL owner：`src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessDataSourceProfileRegistry.cs`。
- Cloud provider 与 provider registry：`src/services/AICopilot.AiGatewayService/BusinessQueries/BusinessQueryProviderRegistry.cs`。
- 中性业务查询入口与确认上下文：`src/services/AICopilot.AiGatewayService/BusinessQueries/BusinessQueryExecutor.cs`、`BusinessQueryContext`；结果只表达成功、空结果、待确认、失败、安全上下文、来源和可信 inline Widget。
- fallback 决策唯一 owner：`BusinessQueryFallbackPolicy`；`BusinessQueryExecutor` 只消费其服务端决策，不把决定权交给模型、provider 或客户端。
- Harness 模型可见的唯一业务查询工具：`src/services/AICopilot.AiGatewayService/Agents/MainChatBusinessQueryTool.cs`。
- 同源 Text-to-SQL runner 与 prompt adapter：`src/services/AICopilot.AiGatewayService/BusinessQueries/CloudReadOnlyTextToSqlFallbackRunner.cs`、`CloudReadOnlyLlmTextToSqlGenerator.cs`；Text-to-SQL 不得单独进入模型工具目录。
- 唯一数据库执行接口、共享 AST guard 和 governed column inspector：`src/services/AICopilot.Services.Contracts/Contracts/IDatabaseConnector.cs`、`src/infrastructure/AICopilot.Dapper/DapperDatabaseConnector.cs`、`src/infrastructure/AICopilot.Dapper/Security/AstSqlGuardrail.cs`、`src/services/AICopilot.Services.CrossCutting/Sql/SqlAllowlistColumnInspector.cs`。
- Cloud readonly 授权脚本：`deploy/enterprise-ai/cloud-readonly/apply-readonly-grants.sql`、`deploy/enterprise-ai/cloud-readonly/check-readonly-grants.sql`。
- Cloud readonly 授权 preflight：`deploy/enterprise-ai/scripts/apply-cloud-readonly-grants.sh`、`deploy/enterprise-ai/scripts/check-cloud-readonly-grants.sh`。
- 关键测试：`AICopilotArchitectureAnalyzerTests` 的 `AIARCH006/AIARCH007`、`ArchitectureBoundaryTests`、`LegacyRetirementArchitectureTests`、`BusinessQueryPipelineTests`、`HarnessMainChatToolTests`、`CloudReadOnlyTextToSqlFallbackRunnerTests`、`SqlGuardrailTests`、`SemanticSqlGenerationTests`、`CloudAiReadClientContractTests`、`SemanticDefinitionTests`、`SemanticSourceStatusDiagnosticsTests`、`ToolSafetyAndApprovalIdentityTests.CloudReadOnlyToolSafety_ShouldRejectForbiddenWriteVerbs`、`PromptGovernanceTests` 和 `SemanticSummaryBuilderTests`。

### 编译型只读门禁

- `AIARCH006` 以 Roslyn symbol/operation 对所有源码方法判定 Cloud root：当前可信身份只包含完全限定的 Cloud AiRead client、`BusinessQueryExecutor`、provider/profile/context/connector/guard 与同源 fallback 类型。命中后追踪完整 call graph、具体实现、interface dispatch、泛型 helper、lambda 和 field/property delegate；同名 fake、DTO 或字符串不得扩大入口。
- Cloud root 可达图中的写边包含完全限定 repository mutation、`SaveChanges*`、`ExecuteNonQuery*`、EF raw/bulk write、`Dapper.SqlMapper.Execute/ExecuteAsync` 以及参数实现完全限定 `ICommand` 的 dispatch。这些边全部是 compiler error；同名 fake 和 SQL 字符串不能代替 symbol identity。
- 只读路径只允许两个精确的内部持久化例外：`IAuditLogWriter` 只记录只读审计；`IModelQuotaReservationStore` 只执行 `TryReserveAsync`、`SettleAsync`、`ReclaimExpiredAsync`。每次真实 `IChatClient` 请求都必须独立预约、结算并记录熔断结果；具体 store、事务 runner 与 Context owner 只由 [DDD 聚合根边界](./DDD聚合根边界.md) 定义。两者都不是 Cloud 业务写权限。
- `AIARCH007` 只接受完全限定符号上的 CloudReadOnly tool safety descriptor，且安全元数据必须同时为 `boundary=CloudReadOnly`、`capability=ReadOnlyQuery`、`readOnlyDeclared=true`；`Diagnostics`、`LocalSuggestion`、`SideEffecting`、缺失值、同名伪类型或其他无法静态证明的动态声明都必须 compiler-error fail-closed。动态 MCP 配置不能因 Analyzer 无法展开就绕过安全契约；注册和每次执行都必须通过同一 `AiToolSafetyPolicy.EvaluateConfigured` 运行时门禁。

## 3. Cloud business plugin 正式路径

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

<!-- AICOPILOT_FALLBACK_POLICY_V1_BEGIN -->
六类 Cloud business plugin 必须优先走上述 typed GET，并统一返回 `Success`、`Empty`、`NeedClarification`、`Unsupported`、`Unavailable` 或 `Unauthorized`。Harness 主聊天的模型可见业务查询表面只能有一个 `BusinessQuery`，Text-to-SQL 不作为独立工具进入模型目录。`BusinessQueryFallbackPolicy` 是唯一 fallback 决策 owner；只有同一 Cloud 来源返回 `Unsupported` 或 `Unavailable` 且查询上下文、数据源 profile 与 capability profile 全部允许时，该 policy 才允许 `BusinessQueryExecutor` 在服务端自动进入受控 Text-to-SQL，模型不得决定、触发或绕过 fallback。`Success`、`Empty`、`NeedClarification`、`Unauthorized`、权限或凭据失败、跨源、MCP 与 Simulation 均不得 fallback。查询确认键固定为 `SessionId`；确认与复用只按该 Session 绑定，重试继续使用同一确认身份键。
<!-- AICOPILOT_FALLBACK_POLICY_V1_END -->

每个 provider 必须为其声明的每个 capability 同时声明非空结果字段契约和敏感字段片段；registry 必须与同一 `SourceKey/SourceType` 的 profile 联合校验 capability，并确保结果契约覆盖 capability profile 的全部敏感字段片段。运行时逐行校验顶层字段，递归检查 dictionary、sequence、`JsonElement`、`JsonDocument` 和可安全序列化 DTO，未知或序列化失败的复杂对象 fail-closed；校验通过后才能进入通用最终上下文 formatter。formatter 不持有 Cloud 专用 schema；MES/ERP 的输出边界由各自 provider capability 结果契约负责。业务数据源绑定必须同时匹配 `SourceKey`、`SourceType`，已确认 `DataSourceId` 时还必须精确匹配该 ID。

查询上下文的来源、能力、业务对象、时间范围和过滤条件只能来自服务端记录的用户确认；Session 存在、模型高置信度、`Device`/`Process`/`ClientRelease` 目录型 target 或空/非空 filters 都不能自动代表用户已确认。同一 Session 先固定已确认 source/sourceId；完全相同 scope 可复用完整确认，改变时间或过滤时只复用未改变的来源、能力和业务对象字段，并对变化字段继续返回 `NeedClarification`。过期、跨 Session 或显式切换来源必须形成新的完整确认。

`Analysis.Recipe.*` 具体数据问题必须在语义规划器、数据提供方、数据库、SQL 生成器和 fallback 之前返回固定禁读边界。`BusinessQueryFallbackPolicy` 基于结构化结果完成 fallback 决策，`BusinessQueryExecutor` 只消费并执行该决策；typed plugin 自身不得直接切换数据源或调用 Simulation。

既有 physical mapping / semantic source status 属于 Direct DB 治理和运维诊断表面，不是正式语义执行授权；其配置、状态 API 或独立测试存在，不得被解释为六类 Cloud-only intent 可以转入 Direct DB。

`production-records` 是生产记录高频读取的唯一 Cloud AiRead 路径。不得新增旁路 endpoint、MCP 写工具或直接 SQL 高优先路径替代它。

生产记录字段必须保持来源真实性：当前 Cloud 正式记录提供 `typeKey/typeName/deviceId/deviceName`、公共记录字段和 schema 化 `fields`，不提供 `processName/stationName/deviceCode/ClientCode`。缺失字段必须保持不存在或空；不得用 `typeName`、`typeKey` 或其他显示字段代填、推断工序、工位或设备编码。

当前生产仅支持 `cp / 正极模切` 与 `ap / 负极模切`。`production-records` 查询除现有参数外允许可选 `plcCode`、`plcName` 精确过滤；AICopilot typed client 必须原样透传。CP/AP 的 `fields` 为 `plcCode`、`plcName`、`clipSlot`、`startTime`、`punchingQuantity`、`punchingSpeed`，回答优先展示中文 `deviceName`、中文 `plcName`、弹夹号、弹夹位 `MG1/MG2`、数量、速度和时间，不展示 Cloud ClientCode。

自然语言固定映射：“正极模切”→`typeKey=cp`，“负极模切”→`typeKey=ap`；紧随其后的设备编号（如“正极模切05”）必须规范化为对应中文 `plcName` 精确过滤。映射不确定或编号不完整时返回 `NeedClarification`，不得猜测设备。

### 3.1 行数唯一 owner 与 sealed plan 边界

- Cloud AiRead 的唯一行数策略是 `CloudAiReadRowLimitPolicy`：`MinimumRows=1`、`MaxRows=100`。Cloud 六类 semantic definition、八个 typed GET、query parameter、result limit 和统一 Cloud provider 都必须引用这一 owner；不得另设 `200`、配置型上限或 endpoint 私有上限。
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
| `/capacity/summary` | `date: DateOnly(yyyy-MM-dd)`、`totalCount: int32`、`okCount: int32?`、`ngCount: int32?`、`dayShiftTotal: int32`、`nightShiftTotal: int32` |
| `/capacity/hourly` | `time: DateTime`、`date: DateOnly(yyyy-MM-dd)`、`hour: int32`、`minute: int32`、`timeLabel: string`、`shiftCode: string`、`totalCount: int32`、`okCount: int32?`、`ngCount: int32?`、`okRate: decimal?`、`plcCode: non-empty string`、`plcName: string?` |
| `/device-logs` | `id: Guid`、`deviceId: Guid`、`deviceName: string`、`level: string`、`message: string`、`logTime: DateTime`、`receivedAt: DateTime` |
| `/production-records` | `recordId: Guid`、`typeKey: string`、`typeName: string`、`deviceId: Guid`、`deviceName: string`、`barcode: string?`、`result: string?`、`completedAt: DateTime?`、`receivedAt: DateTime?`、`fields: scalar object`、`fieldSchema: exact object[]` |

表中的 `?` 表示“key 必须存在、value 可以为 JSON null”，不表示可省略 key。该 required-present nullable 规则覆盖 client release 的 `releaseNotes/publishedAtUtc/deletedAtUtc`、device state 的全部 nullable 字段、capacity summary 的 `okCount/ngCount`、capacity hourly 的 `okCount/ngCount/okRate/plcName`、production record 的 `barcode/result/completedAt/receivedAt`，以及每个 `fieldSchema` entry 的 `unit/precision`。固定 item 的 `DateTime/DateTime?` 与 Cloud DTO 对齐；只有 envelope `asOfUtc` 使用 `DateTimeOffset`。consumer adapter 必须原样保留未知质量事实的 `null`，不得补零或据此伪造良率；小时行的 `okRate` 与 `okCount` 独立：`okRate` 为 `null` 时不得从计数反推，`okRate` 已知时也不得因 `okCount` 为 `null` 而丢弃；多个小时行只在每行良率都已知时按产出量加权汇总。

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
- `deviceCode` 只能用于设备查询或解析，`ClientCode` 只用于 Cloud 内部身份/寻址；不能当 `deviceId` 发给业务读取端点，普通生产回答不得展示 ClientCode。
- `/devices` 支持 `deviceId/deviceCode/processId/keyword/maxRows`，多个条件按 AND 相交，返回设备主数据 `id/deviceCode/deviceName/processId`；不得从该端点读取或生成运行状态、日志级别、`lineName`、`processName` 或 `updatedAt`。
- 自然语言里的设备编码必须先解析成唯一 `deviceId`；只有未截断搜索结果中的唯一精确规范化 `deviceCode` 匹配可以用于解析。零个或多个精确匹配、结果截断或只有模糊命中时要求用户补充正式 `deviceId`，不得扫描分页或选择第一条。
- `Analysis.Device.Status` 只调用 `/device-client-states`，以 `softwareStatus` 为 Cloud 权威派生状态，`runtimeStatus` 保留心跳原值，`lastRuntimeHeartbeatAtUtc` 是唯一 freshness 时间。无心跳设备必须返回 `MissingRuntimeHeartbeat` 行；仅 `asOfUtc - lastRuntimeHeartbeatAtUtc > 24h` 为 `RuntimeHeartbeatStale`，恰好 24 小时不 stale；Stale 不得翻译为 Offline/Stopped。零条只表示授权范围内没有匹配设备。
- `Analysis.Device.Status` 的合法空集不回退，权限/凭据失败不回退；只有统一 plugin 结果为 `Unsupported`/同源 `Unavailable` 时才允许 `BusinessQueryExecutor` 进入同源 Text-to-SQL。Direct DB 设备主数据映射不得连接 `device_logs`，最新日志级别只属于 `Analysis.DeviceLog.*`。
- `Analysis.Process.List` 只调用 `/processes`，支持正式 `processId/keyword/maxRows`；`processCode/processName` 作为搜索语义规范化为 keyword。`Analysis.Process.Detail` 必须至少携带 `processId/processCode/processName` 之一，`keyword` 只能用于 List，keyword-only Detail 必须在 sealed plan/provider/HTTP 前拒绝。`processId` 必须作为 GUID 精确参数发送，并且直查响应只能有一条、不得截断且返回 `processId` 必须与请求完全一致；非 `processId` 搜索分支必须先形成非空 `processCode/processName` exact filters，再在未截断结果中唯一精确命中。空 exact filters、只有模糊命中、零命中、多命中或截断都必须返回明确边界，不得让 `All(empty)` 变成成功、猜测或选择第一条。
- `Analysis.ClientRelease.List` 只调用 `/client-releases`，只允许 `channel/targetRuntime/status/includeArchived`。版本、hash、下载地址、发布说明、归档和发布状态只能逐字段使用 Cloud 返回，不能由模型推断、拼接或补默认值。当前没有 `ClientRelease` 的 governed Text-to-SQL capability profile，因此该能力保持 typed plugin-only，任何结果都不得转入 Text-to-SQL。
- `Analysis.Capacity.*` 只调用两个正式产能 GET；查询范围必须先提供 `deviceId` 或可唯一解析为 `deviceId` 的 `deviceCode`，`plcCode`、`plcName` 只能作为附加精确过滤条件；只提供 `plcCode` 时必须返回 `NeedClarification`，不得进入 typed GET。小时结果中的 `plcCode` 是必填稳定身份，nullable 质量事实必须保持 `null`。
- `Analysis.ProductionData.*` 只调用通用 `/production-records`，允许 `typeKey/processId/deviceId/plcCode/plcName/barcode/result/startTime/endTime/preset/fieldMode/maxRows` 中由 Cloud 正式声明的组合；CP/AP 不新增按工序复制的插件或端点。
- `Analysis.Device.*`、`Analysis.DeviceLog.*`、`Analysis.Capacity.*`、`Analysis.ProductionData.*`、`Analysis.Process.*` 与 `Analysis.ClientRelease.*` 当前都精确绑定 Cloud；领域能力枚举对应 `ProductionRecord`。不得回退其他数据源。Cloud 空集保持空；模糊条件形成 `NeedClarification`；拒绝形成 `Unauthorized`；仅 `Unsupported`/同源 `Unavailable` 可进入同源 fallback 决策。
- `scenarioId`、`from`、`to`、`pilotWindowId`、`boundary` 等 AICopilot 内部试点/执行元数据不得透传 Cloud。
- Cloud 只读请求只能发送 Cloud 端点真实声明的参数。

## 5. Direct DB 和 Text-to-SQL

Cloud Text-to-SQL 只能由统一业务插件返回 `Unsupported` 或同源 `Unavailable` 后受控触发，不能绕过 `Empty`、`NeedClarification`、`Unauthorized` 或凭据失败，也不能切换来源。它必须同时满足：

- 使用已验证的只读 PostgreSQL 账号。
- 表级 `GRANT SELECT` 只覆盖治理白名单表，不使用 `GRANT SELECT ON ALL TABLES`、默认权限、未来表自动授权或写权限。
- SQL 只经过执行咽喉的唯一共享 AST guard 与“已确认 Cloud source + 当前 `BusinessDataCapability`”派生出的 capability profile：单条只读 query、拒绝 DML/DDL/管理语句/多语句；每个物理表必须使用 profile 允许的 `schema.table` 全限定名，表列范围必须完整来自当前能力的 profile。`Device` 不能获得 `DeviceLog`、`Capacity` 或 `ProductionRecord` 的表列，未配置 capability profile 的能力不得生成或执行 SQL。共享 guard 对函数采用业务查询 allowlist，未知函数和系统/文件/配置函数一律拒绝；数据库仍在 read-only session/账号中执行。
- 生产启用 Direct DB 前必须执行 readonly grant preflight。
- 权限错误只能暴露治理白名单内表名和只读权限不足结论，不能输出连接串、role、密码、SQL 原文或非白名单对象。

Text-to-SQL prompt 只负责澄清、PostgreSQL 方言、profile schema 和结构化输出，不维护写操作动词黑名单。它只能暴露批准的表名、列名、类型、join hints 和必要业务描述，不得暴露：

- 连接串、凭据、role/权限细节。
- 样例数据、查询结果、参数值。
- 非白名单表字段、系统字段或敏感字段。
- 用户 prompt 原文、SQL 原文、连接串或 endpoint。

- Direct DB 语义映射中的工序名只能来自只读 `mfg_processes.process_name`。新增 join 表必须同步进入 `CloudReadOnlyGovernedSchema` 的表/列/类型/join hint、所选 source profile 的 security schema、唯一共享 AST guard、只读 role 授权 SQL、授权探针、部署 preflight、RealSource 模板、架构测试和部署文档；缺任一闭环不得读取。
- 创建或轮换 Cloud PostgreSQL 只读账号只能通过用户显式确认的受控自动化执行；只能创建或更新专用 readonly role，并且只授予治理白名单表的 `SELECT`。禁止授予写权限、schema create、superuser、createdb、createrole 或 replication。
- Cloud PostgreSQL readonly role 的权威载体是 `deploy/enterprise-ai/cloud-readonly/apply-readonly-grants.sql` 与 `check-readonly-grants.sql`。生产不得使用 `GRANT SELECT ON ALL TABLES`、默认权限、未来表自动授权或列级/表级混用口径；启用 CloudReadOnly 直连数据库前必须通过 readonly grant preflight。

## 6. 修复重试和审计

- Text-to-SQL 修复重试默认最多 3 次，硬上限 5 次。
- timeout、权限、凭据、非只读、系统表、敏感字段、多语句或写 SQL 默认不可修复、不重试。
- 上一轮失败 SQL 只允许作为当前调用内存参数 `PreviousSqlForRepair` 临时回传给 LLM。
- `PreviousSqlForRepair` 不得进入审计、日志、state、结果、DTO 或持久化对象。
- 成功/失败结果只保存 hash、长度、行数、截断状态、失败分类和安全摘要。

## 7. DeviceLog 和最终回答

- DeviceLog 日志级别必须使用 Cloud PostgreSQL 真实枚举 `ERROR`、`WARN`、`INFO`。
- “错误+警告”“异常分析”等场景必须显式查询多级别，不能只查 `ERROR` 后推断 `WARN` 没有。
- DeviceLog 自然语言中的工序或设备范围必须落到只读 `devices` / `mfg_processes` join 暴露的业务字段过滤；最终回答模型不得按文字自行猜测设备、工序或范围。
- 追问其他日志级别、设备、工序或时间窗口时，必须重新生成并执行本轮 `Analysis.DeviceLog.*` 查询。
- 最终回答只能总结本轮 `query_execution`、`semantic_summary`、返回行数、过滤条件和证据边界；不能基于上一轮回答文本推断未查询数据。
- Widget 和 display blocks 只能重排本轮只读查询事实，不能前端编造指标、Markdown 解析补数据或把建议写成已执行动作。
- DeviceLog 的 `display_blocks` 必须按“结论、关键指标、关键记录、可能原因、建议动作、不能直接执行的动作、查询范围”组织；可能原因必须明确标注为 AI 推断分析，建议动作只能是人工排查建议，不得表达为已执行的控制、下发、写入或修复动作。
- DataAnalysis 最终上下文是独立的不可信消费边界：表结构注释、Text-to-SQL alias 和返回行 key 均不能直接成为最终 JSON 属性名。formatter 必须使用唯一字段标签映射，过滤共享 governed-schema 敏感标识，并让 metadata name/description 与 preview key 保持一致。
- `business_data_preview` 只是扁平业务标量预览；除 string、bool/数值、date、Guid、enum 与等价 JSON 标量外，其余 object/array/collection 不得递归输出、串行化或调用自定义 `ToString()` 透出 nested key/secret，统一用既有脱敏占位表达不可展开值。

## 8. Simulation 边界

- Simulation 只允许作为显式 Development、离线演示或测试资产。
- Development/Simulation 配置只表示该能力可被显式选择，不构成调用授权：请求必须同时显式传入唯一 `SimulationBusiness` `DataSourceId` 和精确 `TextToSql` 模式；provider registry 和 `BusinessQueryExecutor` 不得自动选择 Simulation 数据源或执行模式。
- `appsettings.json` 与 `appsettings.Development.json` 叠加后的默认值都必须保持 `CloudReadonly.Mode=Disabled`、`Simulation.Enabled=false` 和 `CloudAiRead.Enabled=false`；Development 的 Simulation 只能由专用测试 fixture、显式环境变量或显式启动参数逐次开启，不能依赖开发配置文件自动开启。
- 生产基础配置、compose 和部署模板不得携带 `MockOnly=true` 或默认 Simulation 开关。
- Real Cloud 查询失败、为空或未配置时，必须返回 Cloud AiRead / CloudReadOnly 错误或空态，不能降级为 Simulation。
- 任何聊天结果或 Widget 里出现 Simulation 数据时，必须带明确 `sourceMode=Simulation`、`isSimulation=true` 或等价来源标记。
- Simulation 边界测试属于现有 Application、Unit 或 InProcess owner；不维护专用 Simulation runner、静态 case 数或独立发布候选入口。

## 9. 验收命令

以下命令是按受影响范围选择的诊断示例；只能选择与本轮改动直接相关的类，不要求也不得自动追加全仓 required、GoldenEval、Simulation、Live、Web、coverage、mutation 或 deployment 全量：

```bash
dotnet test src/tests/AICopilot.Architecture.AnalyzerTests/AICopilot.Architecture.AnalyzerTests.csproj --filter "AIARCH006|AIARCH007_ShouldRequireControllerMetadataAndCloudReadOnlySafetyMetadata" --no-restore
dotnet test src/tests/AICopilot.DeploymentTests/AICopilot.DeploymentTests.csproj --filter "CloudReadonlyGrantSql_ShouldMatchGovernedRuntimeTables" --no-restore
dotnet test src/tests/AICopilot.UnitTests/AICopilot.UnitTests.csproj --filter "BusinessQueryPipelineTests|CloudReadOnlyTextToSqlFallbackRunnerTests|CloudReadOnlyLlmTextToSqlGeneratorTests" --no-restore
dotnet test src/tests/AICopilot.ApplicationTests/AICopilot.ApplicationTests.csproj --filter "HarnessMainChatToolTests|SemanticSourceStatusDiagnosticsTests" --no-restore
dotnet test src/tests/AICopilot.InProcessTests/AICopilot.InProcessTests.csproj --filter "SqlGuardrailTests|SemanticSqlGenerationTests|CloudAiReadClientContractTests" --no-restore
dotnet test src/tests/AICopilot.ContractTests/AICopilot.ContractTests.csproj --filter "CloudReadonlyChatBoundaryTests" --no-restore
rg -n "CloudAiRead|CloudReadOnly|production-records|PreviousSqlForRepair|CloudReadOnlyGovernedSchema|Simulation|MockOnly" src deploy docs
```

`AICopilot.GoldenEvalTests`、`AICopilot.CloudAiReadLiveTests` 和显式 Simulation/Quality 验收只在用户当前轮授权时运行。Live 验收必须从环境变量读取当前非生产 Cloud BaseUrl/token 和测试实体标识，不允许 StubHandler、手写 JSON 或 Simulation 充当 provider；缺任一变量必须失败，不能 Skip。token 只允许由 Cloud 隔离 E2E 宿主经子进程环境传递，不得进入参数、日志、summary 或仓库。任一仓库生产源码变化后旧 live 结果立即失效。

## 10. 外部依赖

- CloudPlatform 端 AiRead API 的具体实现、权限、nginx/OIDC Provider 部署口径属于 Cloud 项目。
- Cloud PostgreSQL 只读账号创建、授权 SQL 执行和真实 grant 检查需要真实 Cloud 数据库和运维窗口。
- AICopilot 文档和测试不能伪造 Cloud 端 endpoint 已发布、真实数据库已授权或生产查询已通过。
