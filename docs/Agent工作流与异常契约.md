# Agent 工作流与异常契约

本文档约束 AICopilot Agent workflow、Plan/Chat 模式、MCP/Tool/Human-in-the-loop 边界、后端异常和前端错误展示。总计划见 `docs/AI架构治理清单.md`。

## 1. 统一工作流主干

- `AgentWorkflowPipeline` 是用户输入的统一工作流主干，负责意图理解、上下文编排和能力发现。
- Chat 模式和 Plan 模式必须复用统一管线；区别只在出口。
- Chat 出口可以直接回答，或按安全策略执行已允许的低风险只读动作。
- Plan 出口只能生成 `PlanDraft` 草案；用户确认前不得执行 Cloud 查询、MCP 工具、Tool 调用、Worker 入队或其他真实业务动作。
- 用户确认 `PlanDraft` 后才允许转换为 `ExecutablePlan` / `AgentTask`，进入 Skill、Tool、Schema、Guard、审批和 Worker 执行链路。
- Skill、Tool、MCP、Knowledge 或 DataSource 未匹配时，不能阻断服务端能力发现形成 `PlanDraft`；只能在草案中说明能力缺口、降级为路线规划或要求用户补充目标。该规则不允许恢复已退役的前端 `skillCode/preferredToolCodes` 选择入口。
- Plan v2 公共请求的 `pluginSelectionMode/capabilitySelectionMode` 只接受大小写精确的字符串 enum 名；数字 token、未知字符串和大小写变体必须在 HTTP model binding 阶段拒绝，不得进入 stream handler、session/repository、Tool、Cloud 或消息持久化。
- P0 的 `SkillCode/SkillName/SkillRoutingReason` 已从 Plan v2 canonical contract 退役。前端 plan-stream wire 不得发送 `skillCode/preferredToolCodes`；composer 仍有任一旧 Skill/Tool 选择时必须在 HTTP 前显示固定错误并停止，不能静默丢弃、自动猜测 Tool→Skill 或恢复影子路由。后端暂留 legacy request 字段只用于兼容期显式拒绝，并须在 stream handler 读取 session/repository 以及 command coordinator 准备/持久化前返回 `agent_plan_schema_invalid`；公共 SSE 只披露固定通用 schema detail，不泄漏内部拒绝原因。
- Plan v2 的 `262144` UTF-8 byte 上限按“最终含 64 位 SHA-256 digest 的 canonical payload”计算。`AgentCanonicalJsonV1` 是排序、JavaScript 转义、数字规范化、root exclusion、共享结构限制与 byte count 的唯一 owner；Seal 必须先以同长度 64-hex placeholder 做 bounded canonical measure，正好上限允许，首次越界以专用内部信号短路为 `max+1` 并映射 `plan_payload_too_large`，不得让通用 canonical preflight 把业务超限泛化成 `agent_plan_invalid`，也不得放宽现有全局 `Canonicalize` 预检。
- Plan 能力发现和真实 Tool 分支必须共用同一生产安全门禁；只有通过 `AiToolSafetyPolicy` 的 tool 才能进入草案或执行上下文。`GoldenEvalTests` 必须穿过真实 `AgentWorkflowPipeline` 或其正式生产组件，数据集必须版本化并记录变更理由；不得直接调用 leaf policy 自证。

## 2. 能力边界

以下能力必须保持分离：

- Intent routing。
- RAG 知识检索。
- DataAnalysis / Text-to-SQL。
- MCP 工具执行。
- Human-in-the-loop 审批。
- AgentTask worker 执行。

不得为了实现方便把这些能力合成一个大 agent、大 service 或绕过审批/工具边界的隐藏 adapter。`AgentWorkflowTopology` 的 `Tools`、`Knowledge`、`DataAnalysis`、`BusinessPolicy` 分支必须保持显式 fan-out/fan-in，不得拍平成串行或为新能力另起孤立链路。

动态配置的 MCP 目标没有可由调用方 enum、alias、描述或 endpoint 证明的 NonCloud 信任身份。因此 server 与每个 tool 只有 `CloudReadOnly + ReadOnlyQuery + readOnlyDeclared=true` 的精确组合可进入后续动词、MCP hint、schema 和 risk 检查。runtime MCP tool 还必须显式携带独立 canonical `ToolName`，缺失时直接阻断，不得回退到 runtime `Name` 或其它 alias。上述判定必须由聚合注册、runtime builder（含绕过新聚合校验的旧持久化记录）、`AgentWorkflowPipeline` Plan/实时能力发现和 `McpAgentToolExecutor` 每次执行复用同一 `AiToolSafetyPolicy`；禁止 hostname/token heuristic、调用方自报 NonCloud、伪 allowlist、fallback 或影子判定。这一 MCP 信任边界不改变本地非 MCP tool 的正式 capability/risk/审批策略。

### 2.1 分支完成状态与合流门禁

- 四个并行分支必须返回显式 `BranchResult` 完成状态：`Skipped` 表示当前路由没有该分支相关意图；`Empty` 表示相关分支已合法执行但没有真实结果或可用能力；`Succeeded` 表示有可进入最终上下文的真实载荷；`Failed` 表示分支没有完成并携带稳定错误码与安全摘要。禁止再用空字符串、空数组或空对象伪装异常。
- 分支是否 `Required` 必须由本次 routing intents 与对应 executor 的同一套相关性判定得出，不能把某类分支全局硬编码成永远必需或永远可选。路由判定与实际执行过滤条件必须保持一致。
- `Required + Failed` 必须在 fan-in 后、最终上下文聚合前停止最终回答，返回稳定且脱敏的 Chat Error chunk；`Required + Empty` 是合法完成，可以继续合流；可选分支失败不得伪造成成功载荷，也不得进入最终上下文。
- `Skipped`、`Empty`、`Failed` 的载荷一律不得进入 `ContextAggregatorExecutor`；只有 `Succeeded` 可以参与最终回答。调用方取消必须继续向上传播，不能转换成业务空结果或普通失败。
- Final Agent 的持久上下文必须由运行路径内唯一幂等 compensation owner 管理：正常完成只 Set/删除一次，caller cancellation 或业务异常退出时使用不受 caller token 取消的 cleanup token 最多删除一次，不得重放 Agent/Tool 副作用。
- 主异常优先级高于 cleanup 失败：caller cancellation 必须仍向上传播为 `OperationCanceledException`，业务异常也不得被 cleanup 异常覆盖；cleanup 失败只记录 session id 和 exception type，禁止写 raw exception/message。
- 以上状态治理不得改成串行工作流。`Task.WhenAll`、`AgentWorkflowSink` 和四分支 fan-out/fan-in 仍是唯一主干；PlanDraft 的能力发现仍只生成草案，能力缺失或发现失败不得越权执行真实 Tool、MCP、Cloud 查询或 Worker。

Cloud 只读 Agent 当前正式能力限定为：

- `Analysis.Device.List/Detail/Status`：设备主数据以及 Cloud 权威 `softwareStatus`/运行心跳。
- `Analysis.DeviceLog.Latest/Range/ByLevel`：设备日志正式查询。
- `Analysis.Capacity.Range/ByDevice`：产能汇总/小时事实；`Analysis.Capacity.ByProcess` 尚不支持。
- `Analysis.ProductionData.Latest/Range/ByDevice`：正式生产记录。
- `Analysis.Process.List/Detail`：工序主数据列表与唯一精确详情。
- `Analysis.ClientRelease.List`：Cloud 返回的客户端发布版本列表。

以上能力必须复用统一语义定义、`CloudReadonlyAgentPlanService` 和唯一 Cloud AiRead 客户端；成功、合法空集、语义规划失败或 Cloud 数据源不可用时都必须返回真实边界，不得回退 Direct DB、Text-to-SQL、Simulation、MCP 或隐藏适配器。`PlanAgentTaskCoordinator` 只能创建和维护草案，不得持有查询客户端或执行查询；语义 intent 只能在用户确认草案后创建，运行时工具只能在确认后的执行链调用。

`Analysis.Recipe.*` 的具体配方数据请求必须在调用语义规划器前返回禁读边界，即使规划器本会失败也不能进入 provider、数据库或 fallback。Chat 语义执行器只持有 Cloud AiRead 客户端、语义规划器和日志器；Direct DB、physical mapping、SQL generator、数据库审计与 Text-to-SQL fallback 只能留在各自独立治理链，不能重新挂回正式语义执行器。

### 2.2 DataAnalysis 最终上下文边界

- `analysis.metadata` 与 `business_data_preview` 必须共用同一份大小写不敏感字段标签映射；同一 raw field 的 metadata name/description 和 preview property key 必须一致，重名标签只在该唯一入口稳定加后缀。
- raw field 为 SQL、表/视图、数据源、数据库、host/user 等 formatter 内部显示字段，或命中 `CloudReadOnlyGovernedSchema.BlockedFieldFragments` 的 key/credential/secret/token/password 等共享敏感标识时，必须整项丢弃，不能换一个业务名后继续输出其值。
- 标签候选只允许 metadata description 再回退 raw field；指令型/内部文本、控制字符、换行或超过 80 字符的候选不得成为 JSON key，两个候选都不安全时使用固定业务 fallback。
- preview 只承载最多 3 个可识别 dictionary row 的扁平标量。值只走唯一 `SanitizeValue` 入口；JSON null/bool/number/string 可映射为同等标量，CLR 只显式允许 string、bool/数值、date、Guid 和 enum。JSON object/array 及其余任意 CLR object/collection 一律输出既有脱敏占位，不调用自定义 `ToString()` 透出内容，不递归展开第二层 key。
- Semantic/FreeForm Widget 在 formatter 之前由各自真实 plan/summary/rows 生成，不消费最终 prompt label map；不得因最终上下文治理复制第二套 Widget 标签或值清洗器。

### 2.3 P0 产物检查点与 Tool 输出边界

- 声明产物目标的 Plan 必须且只能有一个最后步骤 `finalize_artifacts`，其 `StepType=Finalize`、`RequiresApproval=true`；该步骤是生命周期检查点，不是 provider tool，不能交给 built-in、MCP 或 mock executor 伪造执行成功。
- P0 runtime 只允许 `BuiltInOnly`，没有可信 PlanCompiler 时生产 `ExecutablePlan` 必须 fail-closed；Draft 只披露 `plan_compiler_unavailable`，不得把测试用 fresh-read/downstream harness 带入生产注册。
- Tool output 必须先通过注册表的 closed strict schema，才可记录 execution、step 或 run 成功。持久化 durable output 只保留规范化、版本化的安全 payload，不得保存 provider raw output；ArtifactWorkspace 文件/aggregate 的原子 staging、补偿、provenance 与 reconciliation 仍属于 P1，本 P0 契约不得宣称已经闭合。

## 3. Cloud 写入禁止

- Agent workflow、MCP、Tool、后台任务、直接 SQL 和隐藏 adapter 均不得创建、修改、删除、补录、审批、派发或触发 Cloud 业务数据。
- Human-in-the-loop 不是 Cloud 业务写入授权。
- 如果未来需要 Cloud AI-facing 写接口，必须由用户明确批准新的跨仓库接口契约、权限模型、审计模型和回滚策略；不得在 AICopilot 内部先行实现。

## 4. 异常响应契约

后端未知异常必须走稳定 ProblemDetails：

- 必须返回稳定 `code`、`detail`、`userFacingMessage` 和 `traceId`。
- `code` 与 `traceId` 是大小写不敏感的保留 extension key；descriptor extensions 中的 `Code`/`TRACEID` 等任意大小写变体必须在复制时丢弃，再分别由 `ApiProblemDescriptor.Code` 与当前 `HttpContext.TraceIdentifier` 以唯一 canonical `code`/`traceId` 写入，调用方不能通过 extensions 注入伪值或歧义键。
- 用户可见文案必须是安全摘要，不能包含 raw exception message、SQL、prompt、token、endpoint、连接串、密码、API key 或内部 provider 细节。
- `UseCaseExceptionHandler` catch-all 不得把原始 exception 对象交给 logger 形成敏感日志。
- 新增、删除或重命名错误码时，必须同步更新 `docs/frontend-integration-contract-package-2026-05-17.md` 并运行错误码目录测试。
- `agent_plan_invalid`、`agent_plan_schema_invalid` 与 `plan_payload_too_large` 的公开 `code/detail/userFacingMessage` 必须由同一共享披露策略固定产生，REST unhandled、普通 `ReturnResult`、SSE exception/Result、AgentEvent 和 queue/DTO 不得从 exception `SafeDetail`、`ApiProblemDescriptor.Detail`、string error 或任何 Plan 可控文本派生用户可见内容。
- `Result.Errors` 是有序多项序列：出口必须按序选择首个可公开的 Plan descriptor，安全 match 只能携带固定 disclosure 与精确 non-empty `Guid taskId`，不得携带原始 descriptor/extensions。没有 descriptor 时 Plan draft 按固定 `agent_plan_invalid` 处理；只有未知 descriptor 时保留原首项的普通 fallback 语义。
- SSE `AgentEvent` payload 字段名固定为 `stage/code/detail/recoverable/suggestedAction/metadata`；不得因全局 serializer 默认而输出 PascalCase 变体，也不得为修单一 event 去改全局 JSON 契约。

### 4.1 提交结果未知

- repository 使用 durable commit marker 处理 COMMIT ACK 丢失；fresh verification 无法确认 marker 时，HttpApi 返回 HTTP 503、`code=persistence_commit_outcome_unknown`、安全 `detail/userFacingMessage`、trace id 和非敏感 commit id。
- 该响应表示“写入可能已经提交”，不是确定失败。调用方不得自动重试同一业务动作；应先按 commit id 对账，再决定返回既有结果、补偿或人工重试。
- 日志只记录 trace id、commit id、exception type 和 inner exception type，不记录 raw exception message、连接串、SQL、文件路径或业务载荷。
- RAG `UploadDocument` 与 AiGateway SessionTemp/AgentInput 数据库绑定上传必须在物理文件前写 `.persistence/file-reconciliation` 日志，并让数据库事务复用同一 commit id；收到该异常时必须保留文件和日志，不能按普通回滚失败删除。知识库文件唯一写入口是 RAG Document API；已停止的 AiGateway KB shadow scope 不得恢复，历史行/列由 `AI-PERSIST-01e` 在维护窗口清理。
- 请求侧持有以 commit id 派生的 PostgreSQL advisory lease，DataWorker 对账必须取得同一 lease 后才能处理；有 marker 时保留文件并删除日志，无 marker 时删除文件后再删日志。HttpApi 与 DataWorker 必须共享 `/var/lib/aicopilot`，对账日志损坏时 marker 过期清理 fail-closed，禁止手工或 cron 绕过。
- RAG 文档删除事件必须加入同一对账边界：按 storage path 查找 journal，有 pending 记录时争用相同 commit lease、锁内复查并先持久退休 journal 后再删除文件；journal 不可读或 lease 活跃必须由消息系统重试。文件名、审计和结构化日志只能使用跨平台安全 basename，禁止保存原始客户端路径。
- commit marker 默认保留 30 天并按 `created_at_utc` 索引；保留期必须长于对账延迟，仍有 journal 的 marker 不得删除。

## 5. 日志和持久化脱敏

生产路径日志、审计、任务失败摘要和持久化失败原因必须只记录安全字段：

- traceId / correlationId。
- exception type / error type。
- failure code / reason code。
- SQL length / SQL hash。
- query hash / question hash。
- intent routing response length / SHA-256 / response type / parse state。
- 固定业务错误码和固定用户文案。

不得记录：

- raw exception message。
- raw exception 对象，即 `LogError(ex, ...)`、`LogWarning(exception, ...)`、`LogError(e, ...)`、`LogWarning(cleanupException, ...)` 等把异常变量作为 logger 首参的重载。
- SQL 原文、用户 prompt、参数值。
- token、API key、密码、连接串。
- endpoint、sourceName、表名、视图名、内部字段。
- 原始工具参数、原始工具结果行或未脱敏 provider 返回。
- intent routing 原始响应、intent reasoning、用户 prompt 或查询原文；路由诊断只能记录长度、SHA-256、类型和解析状态。

少量 `ex.Message` 只能作为内部分类器输入；输出仍必须是固定安全文案、hash、code 或 failure classification。

## 6. 前端错误展示

- 普通 API、SSE open/error、AgentEvent、ApprovalRequest、AgentTask、Chat Error chunk、OIDC、auth、RAG、Config、artifact、upload、route guard 等失败路径必须进入会话错误栏、页面错误栏、dialog error 或安全 fallback。
- 前端必须优先展示后端 ProblemDetails 的 `userFacingMessage`、validation errors、`detail`、`title`。
- 未知 Chat Error code 不得直接展示 raw `detail`。
- 不允许用户操作失败只 `catch {}` 或只写 console 而没有可见状态。
- 纯解析 fallback 可以降级展示或记录安全摘要，但不能伪造成功状态。

## 7. 运行详情

- 运行详情默认折叠。
- 运行详情只能展示工具名、查询次数、返回行数、截断状态、Widget 类型、业务过滤条件和安全摘要。
- 运行详情不得展开 SQL 原文、连接串、password、token、endpoint、sourceName、tableName、databaseName、内部路径、原始工具结果行或未脱敏错误。
- 运行详情不是审批、AgentTask、Cloud 查询或 Widget 的权威状态源；权威状态必须来自对应聚合和 session timeline 投影。

## 8. 源码归属

- 统一工作流：`src/services/AICopilot.AiGatewayService/Workflows/AgentWorkflowPipeline.cs`。
- PlanDraft / ExecutablePlan：`src/services/AICopilot.AiGatewayService/AgentTasks`。
- Tool / MCP / approval：`src/services/AICopilot.AiGatewayService/Tools`、`src/services/AICopilot.McpService`、`src/infrastructure/AICopilot.Infrastructure/Mcp`。
- 后端错误边界：`src/hosts/AICopilot.HttpApi/Infrastructure/UseCaseExceptionHandler.cs`、`src/shared/AICopilot.SharedKernel/Result`。
- SQL/DataAnalysis 脱敏：`src/infrastructure/AICopilot.Dapper`、`src/services/AICopilot.DataAnalysisService`。
- runtime/provider/worker 脱敏：`src/infrastructure/AICopilot.AiRuntime`、`src/services/AICopilot.AiGatewayService/AgentTasks`、`src/services/AICopilot.AiGatewayService/Workflows/Executors`。
- 前端错误：`src/vues/AICopilot.Web/src/services`、`src/vues/AICopilot.Web/src/stores`、`src/vues/AICopilot.Web/src/protocol`、`src/vues/AICopilot.Web/src/views`。
- 运行详情：`src/vues/AICopilot.Web/src/protocol/runtimeDetails.ts`、`src/vues/AICopilot.Web/src/components/chat/MessageRuntimeDetailsPanel.vue`。

### 8.1 编译型 Agent / 权限门禁

- `AIARCH004` 使用跨方法 call graph 追踪任何可能减少 enabled Admin 的路径，包括 interface dispatch、泛型 helper、inline/stored 与 field/property 中的 lambda/method-group。Field/property initializer、constructor assignment 和 property getter return 必须在 CompilationEnd 统一解析为 edge-aware caller→delegate 边，把 synthetic transaction-delegate edge 与真实 invocation/delegate `Invoke` edge分开；同一 target 即使曾被事务调用，只要又从同一 handler 或另一个 handler 直接调用，仍必须判定 mutation 可在事务外到达。Root 既包含外部可达入口，也包含源码图中无 incoming edge 的 protected `BackgroundService.ExecuteAsync`、internal seeder 与 internal type public entry；不能把生产入口改 internal/private 换绿，transaction private helper 则必须由 synthetic incoming 归属到 caller，避免 method-global 假豁免或重复误报。真实 mutation 必须位于完全限定 `ITransactionalExecutionService` 的 transaction delegate 内，且完全限定 enabled-admin invariant guard 在同一执行块/路径上词法支配并先于 mutation。事务、guard 和 mutation 互不相交，guard 位于 mutation 之后，或 stored/member delegate 事务执行后再次直接 `Invoke`，都必须 compiler-error fail-closed；运行时真实 PostgreSQL 锁/竞态测试仍负责证明事务与 retry 语义。
- `AIARCH005` 要求具体 Agent plugin 显式 override `Description` 和 `ChatExposureMode`，并至少暴露一个带 `DescriptionAttribute` 的实例 tool。组件扫描、DI activation 和加载只属于 `AICopilot.AgentPlugin.Runtime`；零调用插件、静态假 tool、宿主内伪业务成功路径和生产 Fake/Stub/Test executor 必须物理删除。
- 生产树中唯一 test-double 例外是完全限定类型 `AICopilot.AiGatewayService.AgentTasks.MockMcpAgentToolExecutor`：它必须保持 `internal`，只能在 `Environment.IsDevelopment()` 且 `AiGateway:MockMcp:Enabled=true` 时注册，输出必须带 mock/simulation 事实且不能执行外部副作用。同名类型、换 namespace、wrapper/adapter 或第二个 mock executor 均不在例外内。
- `AIARCH007` 只按完全限定 symbol identity 识别 request interface、`AuthorizeRequirementAttribute`、MVC `ControllerBase` / HTTP action attribute / `[Authorize]` / `[AllowAnonymous]`、tool descriptor 和契约例外；同名类型、伪属性、attribute alias 或换 namespace 都不得扩大识别面。Service 的公开 command/query/stream request 必须显式声明 `AuthorizeRequirement`，stream 没有例外；只有 `FinalizeCloudOidcLoginCommand`、`LoginUserCommand`、`GetCurrentUserProfileQuery`、`GetInitializationStatusQuery` 四个完全限定 Identity 公开请求例外。资源所有权/动态权限不得用不真实的单一静态权限换绿；只有 `GetArtifactWorkspaceQuery` / `DownloadArtifactQuery -> ArtifactWorkspaceQueryCoordinator` 和 `ApproveAgentApprovalCommand` / `RejectAgentApprovalCommand -> AgentApprovalDecisionCoordinator` 四个完全限定 `ResourceAuthorizationOwner` 对，并由 coordinator 执行真实 owner/approval-type/privileged permission 校验。HttpApi Controller action 必须在类或方法上显式 `[Authorize]` / `[AllowAnonymous]`。
- 上述边界由 `AICopilot.Architecture.Analyzers` 在所有生产编译中以 `Error + IsEnabledByDefault + NotConfigurable` 执行，CompilationEnd 规则保留 `CompilationEnd` tag；`AICopilot.Architecture.AnalyzerTests` 保持正/反语义 fixture 和真实临时 csproj 编译/suppression fixture，inventory 同时扫描 `NoWarn`、Analyzer 关闭、`.editorconfig/.globalconfig` severity、`#pragma warning disable`、`SuppressMessage/UnconditionalSuppressMessage`；不得恢复可降级 descriptor 或同义 Regex/字符串影子门禁。

## 9. 验收命令

以下命令用于 Agent/异常专题定向诊断；任务完成仍必须对账 inventory 中全部 required runner、Web 和 deployment behavior，不得用 filter 结果代替。

```bash
dotnet test src/tests/AICopilot.WorkflowTests/AICopilot.WorkflowTests.csproj --no-restore
dotnet test src/tests/AICopilot.ApplicationTests/AICopilot.ApplicationTests.csproj --filter "ToolRegistryApplicationTests|TextToSqlReadOnlyTests|AuthorizationPipelineBehaviorTests" --no-restore
dotnet test src/tests/AICopilot.ContractTests/AICopilot.ContractTests.csproj --filter "ChatErrorContractTests" --no-restore
dotnet test src/tests/AICopilot.InProcessTests/AICopilot.InProcessTests.csproj --filter "UnhandledApiExceptionPolicyTests" --no-restore
dotnet test src/tests/AICopilot.ToolPlugin.ConformanceTests/AICopilot.ToolPlugin.ConformanceTests.csproj --no-restore
dotnet test src/tests/AICopilot.Architecture.AnalyzerTests/AICopilot.Architecture.AnalyzerTests.csproj --no-restore
cd src/vues/AICopilot.Web && npm run test:unit -- chatErrorStore runtimeDetails
rg -n "Log(Critical|Error|Warning|Information|Debug|Trace)\\(\\s*[a-zA-Z_][a-zA-Z0-9_]*\\s*," src/hosts src/infrastructure src/services src/vues/AICopilot.Web/src
```

## 10. 外部依赖

- 本契约不授权 Cloud 业务写接口，也不替代 CloudPlatform 权限、审计或接口契约。
- 真实生产日志、前端线上错误和 AgentTask worker 行为仍需发布后通过日志、trace、UI 和任务记录验收。
