# Agent 工作流与异常契约

本文档约束 AICopilot Agent workflow、Plan/Chat 模式、MCP/Tool/Human-in-the-loop 边界、后端异常、前端错误展示和后端拥有的错误码目录。未完成架构方向见 `docs/AI架构路线图.md`；历史治理状态只通过 Git 追溯。

## 1. 运行主干

- 当前主聊天 SSE 与批准续流只允许构造一个 `HarnessAgent`；`ChatStreamHandler`、`ApprovalDecisionStreamHandler` 不得再调用 `AgentWorkflowPipeline` 或保留双活兼容路由。旧 workflow 源码在物理删除前只服务仍有明确 owner 的非主聊天路径，不能重新接回当前聊天。
- Harness 的 `Plan` / `Execute` 是同一对话 AgentSession 的运行模式，不能与 durable `PlanDraft` / `ExecutablePlan` / `AgentTask` 状态机混用。新会话默认 `Plan`；Plan 只允许 Harness Todo 与 `mode_get`，不得向模型发送 Cloud、RAG、MCP、`BusinessQuery` 或其它外部/业务工具。
- `Execute` 只能根据当前认证用户、会话和实时安全门禁动态提供工具；Chat 可以直接回答，或执行已允许的低风险只读动作。模式切换不能扩大工具注册、安全元数据、权限或审批边界。
- Durable Plan 出口仍只能生成 `PlanDraft` 草案；用户确认前不得执行 Cloud 查询、MCP 工具、Tool 调用、Worker 入队或其他真实业务动作。用户确认无 gap 后才允许转换为 `ExecutablePlan` / `AgentTask`，进入 Tool、Schema、Guard、审批和 Worker 执行链路。
- Tool、MCP、Knowledge、DataSource、Provider 或资源未匹配时，不能阻断服务端能力发现形成 `PlanDraft`；必须形成显式 capability gap，带 gap 的草案保持 node-free、不可确认且不得入队。
- Plan v2 公共请求的 `pluginSelectionMode/capabilitySelectionMode` 只接受大小写精确的字符串 enum 名；数字 token、未知字符串和大小写变体必须在 HTTP model binding 阶段拒绝，不得进入 stream handler、session/repository、Tool、Cloud 或消息持久化。
- Plan v1 只读兼容保留至 `2026-12-31`：已完成的 v1 历史任务必须仍可读取、展示和审计，但不得执行、重试、克隆、重新确认或转换成 v2。非终态 v1 任务只能取消后以 v2 重建；到期删除读取兼容前必须另行完成生产存量盘点与迁移裁决，不能因新执行轨已是 v2 就提前删除。
- `SkillDefinition`、`IAgentDynamicPlanner` 及其兼容字段/API 已从生产轨物理退役。Plan 请求只允许 `pluginSelectionMode/selectedPluginIds/capabilitySelectionMode/requestedCapabilityCodes/knowledgeBaseIds/uploadIds/artifactTargets`；这些选择是编译上限，唯一 `AgentPlanCompiler` 与安全门禁必须取交集，不得恢复 Skill 选择、preferred ToolCode、alias/wrapper 或第二套影子编译器。
- Plan v2 的 `262144` UTF-8 byte 上限按“最终含 64 位 SHA-256 digest 的 canonical payload”计算。`AgentCanonicalJsonV1` 是排序、JavaScript 转义、数字规范化、root exclusion、共享结构限制与 byte count 的唯一 owner；Seal 必须先以同长度 64-hex placeholder 做 bounded canonical measure，正好上限允许，首次越界以专用内部信号短路为 `max+1` 并映射 `plan_payload_too_large`，不得让通用 canonical preflight 把业务超限泛化成 `agent_plan_invalid`，也不得放宽现有全局 `Canonicalize` 预检。
- Plan 能力发现和真实 Tool 分支必须共用同一生产安全门禁；只有通过 `AiToolSafetyPolicy` 的 tool 才能进入草案或执行上下文。`GoldenEvalTests` 必须穿过真实 `AgentWorkflowPipeline` 或其正式生产组件，数据集必须版本化并记录变更理由；不得直接调用 leaf policy 自证。

### 1.1 Harness 与模型调用边界

- 主聊天固定使用 `Microsoft.Agents.AI.Harness` / `Microsoft.Agents.AI` `1.16.0`；每轮最多 8 次模型调用。必须关闭 FileMemory、WebSearch、AgentSkills、BackgroundAgents、LoopEvaluators 与 compaction，不注册 FileAccessStore、Shell 或文件 Artifact；聊天只保留 inline 文本和图表。
- 运行时必须分为“模型端点/认证/配额/熔断/遥测的轻量 `IChatClient` 工厂”和“仅供主聊天使用的 Harness 工厂”。Text-to-SQL、分类与结构化生成继续直接使用轻量客户端，禁止嵌套 Harness；主聊天依赖不得恢复 `Microsoft.Agents.AI.Workflows`。
- `ToolSurfaceGuardChatClient` 是发给真实模型前和收到模型工具调用后的最内层强制边界：请求侧按当前模式过滤工具并始终删除 `mode_set`，响应侧对未公开工具、`mode_set`、伪造批准响应或模式不允许的调用 fail-closed。该安全性不能依赖系统提示词。
- 模型供应商健康、endpoint pool、认证、配额、熔断与遥测必须作用于每一次真实 `IChatClient` 调用；构造 Agent 不能记录熔断成功，外层一次聊天 invocation 也不能只预约一次配额。流式中断或结果未知必须以已派发、用量未知的保守状态结算预留额度。

### 1.2 AgentSession 与模式

- 每个新 Session 必须同步创建一条 `agent_session_states` 一对一状态：绑定 `session_id + user_id + tenant_id`，`agent_schema_version=1`，保存受 ASP.NET Core Data Protection 固定用途字符串认证加密的完整 AgentSession、`Ready|Running|Interrupted`、`active_turn_id`、乐观并发版本与创建/更新/过期时间。单条明文状态上限 2 MiB，30 天滑动 TTL；状态、密钥和解密错误不得写入日志。
- 运行前必须在会话锁内写 `Running + turn id`；ChatHistoryProvider 在每次真实模型调用后 checkpoint。正常完成、等待批准和模式切换都必须序列化完整 AgentSession。取得锁后发现遗留 `Running` 时只允许转为 `Interrupted`，不得自动恢复、重放模型或重放工具。
- 缺失旧状态、schema 不匹配、损坏、超限或过期统一返回 `agent_session_reset_required` 并提示新建会话；`Interrupted` 必须显式展示且不能自动继续。暂时缺少可用模型配置不能把结构有效的状态误标为损坏。
- 模式切换唯一入口为认证的 `PUT /api/aigateway/session/{sessionId}/agent-mode`，请求必须携带 `plan|execute` 与 `expectedVersion`；仅 owner 可调用，并通过 Harness 官方 `SetModeAsync` 修改。活跃 turn、待批准、Interrupted 或版本冲突必须拒绝，竞争返回 `agent_session_version_conflict`。前端展示服务端真实模式，活跃 turn 或待批准期间禁用切换。

### 1.3 主聊天工具批准

- 工具从注册表到模型包装必须保留 `RequiresApproval`、`RiskLevel`、`RequiredPermission`、`AuditLevel`、`DataBoundary` 和 `SchemaVersion`；经过当前用户权限、安全门禁后才可包装为批准中间件可识别的函数。删除旧审批实现不得删除或弱化这些元数据。
- Harness 必须设置 `DisableToolAutoApproval=false`、`AutoApprovalRules=[]`；禁止“不再询问”，后端必须拒绝 `AlwaysApproveToolApprovalResponseContent` 或任何等价永久批准信号。
- 每次批准必须绑定用户、租户、session、request id、toolCallId、规范工具身份、工具 schema 版本和 canonical 参数 SHA-256 摘要；续流前重新验证全部绑定。参数摘要漂移、跨用户/租户/session、重复/外来 toolCall 或 Interrupted 后的旧批准全部 fail-closed。
- 主聊天只允许批准 AICopilot 自身、可逆、幂等或结果可查询的动作。Cloud/MES/ERP 写入、设备启停、生产控制、不可逆或结果不可查询动作即使用户点击批准也继续硬阻断。

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

以上能力必须进入统一业务数据查询管线：确认 Cloud 数据源、能力、业务对象、时间和过滤条件后，先调用对应 Cloud 业务插件，并返回 `Success`、`Empty`、`NeedClarification`、`Unsupported`、`Unavailable` 或 `Unauthorized`。`Success`/`Empty` 直接终止，`NeedClarification` 继续询问，`Unauthorized` 禁止绕过；只有 `Unsupported` 或同源 `Unavailable` 才可调用同一 Cloud profile 的受控 Text-to-SQL。Chat 在首次业务查询范围确认后直接复用该确认，不得为 fallback 再制造第二次确认；Plan/Worker 必须由已确认计划明确选择 `TextToSql`。禁止跨源、Simulation、MCP 或隐藏适配器 fallback。`PlanAgentTaskCoordinator` 只能创建和维护草案，不得持有查询客户端或执行查询；语义 intent 只能在用户确认草案后创建，运行时工具只能在确认后的执行链调用。

`Analysis.Recipe.*` 的具体配方数据请求必须在调用语义规划器前返回禁读边界，即使规划器本会失败也不能进入 provider、数据库或 fallback。Chat、Plan 和 Worker 的真实业务查询必须复用同一 provider registry、确认上下文和 fallback policy；语义入口不得再维护一套 Cloud 专用 SQL guard、写动词清单、Runner 或隐藏回退。Text-to-SQL 的生成/repair 可以保持独立实现，但只能由统一策略在同源且结果类型符合条件时调度，最终 SQL 统一经过执行咽喉的 profile-aware AST guard 和只读数据库账号。

### 2.2 DataAnalysis 最终上下文边界

- `analysis.metadata` 与 `business_data_preview` 必须共用同一份大小写不敏感字段标签映射；同一 raw field 的 metadata name/description 和 preview property key 必须一致，重名标签只在该唯一入口稳定加后缀。
- raw field 为 SQL、表/视图、数据源、数据库、host/user 等 formatter 通用内部显示字段时必须整项丢弃。业务插件结果在 formatter 之前必须通过当前 provider/capability 声明的允许字段与敏感字段契约；Text-to-SQL 结果必须通过当前 source profile。formatter 不得静态依赖 Cloud schema，也不能把被上游契约拒绝的字段换一个业务名继续输出。
- 标签候选只允许 metadata description 再回退 raw field；指令型/内部文本、控制字符、换行或超过 80 字符的候选不得成为 JSON key，两个候选都不安全时使用固定业务 fallback。
- preview 只承载最多 3 个可识别 dictionary row 的扁平标量。值只走唯一 `SanitizeValue` 入口；JSON null/bool/number/string 可映射为同等标量，CLR 只显式允许 string、bool/数值、date、Guid 和 enum。JSON object/array 及其余任意 CLR object/collection 一律输出既有脱敏占位，不调用自定义 `ToString()` 透出内容，不递归展开第二层 key。
- Semantic/FreeForm Widget 在 formatter 之前由各自真实 plan/summary/rows 生成，不消费最终 prompt label map；不得因最终上下文治理复制第二套 Widget 标签或值清洗器。

### 2.3 Plan 编译、产物检查点与 Tool 输出边界

- 声明产物目标的 Plan 必须且只能有一个最后步骤 `finalize_artifacts`，其 `StepType=Finalize`、`RequiresApproval=true`；该步骤是生命周期检查点，不是 provider tool，不能交给 built-in、MCP 或 mock executor 伪造执行成功。
- 当前生产 Plan 只允许 `BuiltInOnly`；唯一权威 `DeterministicAgentPlanCompiler` 只消费同一版本 `AgentIntentRegistry` 的 typed candidate 与服务器冻结快照。它可以生成显式 `LinearV1`，或在存在多个独立只读 Evidence 根时生成受限 `DagV1`；`DagV1` 只允许固定节点类型、2–4 并行、显式 `dependsOn`、`AllRequired/OptionalBestEffort` 合流和确定性节点 ID，不得另建 general compiler、模型自由拓扑或 Runtime 猜测 profile。缺少执行快照、资源、工具或能力必须披露稳定 gap；任何带 gap 的 Draft 都保持 node-free、不可确认、不可入队。
- 同一个 Plan 可冻结多个 `cloudReadonlyIntents`，每个 `CloudReadNode` 必须精确绑定自己的 semantic intent、plan digest、scope、time range 和 row limit；`Analysis.Device.Status` 的健康评估只能是该状态节点的确定性子节点。插件 `Empty`、`NeedClarification`、`Unauthorized` 不得创建或调度 fallback；只有 `Unsupported` 或同源 `Unavailable` 可在确认计划明确选择后进入同一 Cloud profile 的 Text-to-SQL，且始终禁止 Simulation、MCP 或跨数据源回退。
- 每个成功 `NodeRun` 必须在同一 fenced checkpoint 写入唯一 sealed Evidence；恢复只能从已授权 Evidence 与 checkpoint 重建，不得读取子 Agent 原始对话。并行根各自在独立 state 中运行，fan-in 只按 canonical 规则合并 Evidence payload；required 失败阻止下游，optional 缺失必须显式保留质量标记。
- `AgentReasoningNode` 派生深度固定为 1，只消费 `EvidenceOnly + SafeSummary`，不得 spawn、继续 Tool 调用、扩权或把 `LlmInference/Recommendation` 伪装成 `ObservedFact/DerivedFact/ModelPrediction`。最后一次 recovery 仍不能产生合法 typed output 时节点失败。
- 显式 `Development + CloudReadonly:Mode=Simulation + Simulation.Enabled=true + AlwaysMarkAsSimulation=true` profile 仍只能把唯一已授权 `SimulationBusiness` 数据源编译为固定的 `query_business_database_readonly → summarize_business_query_result → generate_markdown_report → finalize_artifacts` 四步图。该 profile 不得接 Cloud、MCP、Plugin、上传或 RAG，不得混合/回退 Real/Cloud 数据源；Plan 确认、入队和 Worker 每次 fresh-read 都必须重新校验 Simulation profile，配置关闭后旧计划立即 fail-closed。
- Tool output 必须先通过注册表的 closed strict schema，才可记录 execution、step 或 run 成功。持久化 durable output 只保留规范化、版本化的安全 payload，不得保存 provider raw output。ArtifactWorkspace 的 workspace 初始化、draft 多文件创建、版本归档、当前文件替换和 final 文件集必须统一经过 file-set stage/journal、manifest hash、fencing token、数据库 checkpoint、commit marker、rollback/reconciliation 和 orphan cleanup；数据库或文件系统任一结果未知时不得宣称完成。
- Markdown、HTML、PDF、PPTX、XLSX、图表 payload 和 Chat 追问必须绑定同一最终 `EvidenceSetDigest`，不得各自重算或只取最后一次 Cloud 查询。Chat 只能通过显式 `ReferencedAgentTaskId` 引用当前用户、当前会话内 `Completed` 任务的最新 `Succeeded` attempt；服务端必须验证 finalization checkpoint、全部 durable Evidence scope/expiry/seal 和完整 lineage，仅注入 bounded safe summary/findings/citations。不得自动选“最近任务”，不得从旧回答文本反推事实；路由命中新设备、工序、级别或时间范围时必须走本轮新只读查询，不得混入旧 EvidenceSet。

### 2.4 最终产物审批、关单与恢复

- 严格审批元组必须唯一绑定 `taskId + workspaceId/workspaceCode + finalStepId + finalOutputApprovalId + activeRunAttemptId + task/node fencing token + EvidenceSetDigest + manifest digest + Artifact binding/file SHA-256`。任一标识缺失、外来、重复或在审批后漂移，都必须返回 `agent_finalization_state_conflict`，不能按相似 workspace、最近 attempt 或最后一个审批猜测。
- final-output `decision proof` 至少包含唯一审批状态、决策人、创建时间、决策时间和目标 workspace；`Pending` 不得携带决策字段，`Approved/Rejected` 必须有非空决策人与不早于创建时间的决策时间。审批请求必须晚于所有产物生成步骤完成；只有 `Approved` 可以触发 durable resume，`Rejected` 立即终结任务并返回 `agent_approval_rejected`。
- 同一任务和 workspace 只能存在一个 final-output 审批。两个审批人竞争决定、批准与拒绝竞争、重复点击或迟到请求只能有一个决策胜出；相同已决结果可幂等读取，不同决策必须返回 `agent_approval_state_conflict`，不得覆盖首个 decision proof，也不得产生第二个关单队列项。
- 批准不是关单。审批请求完成后任务仍为 `WaitingFinalApproval`，attempt 先保持无 lease 的 `WaitingApproval`；durable Worker 重新 claim 后才可校验元组并执行 file-set stage 与 fenced checkpoint。只有 Artifact、Workspace、最终步骤、attempt、任务和 file-set operation 在同一权威提交中一致到达终态，任务才是 `Completed`。
- checkpoint 损坏、manifest/file binding 不一致、lease 过期、fencing 失效、提交结果未知或恢复时间线断裂时必须 fail-closed；不得从 Timeline、前端状态或目录是否存在反推完成。幂等恢复只能 fresh-read 权威 checkpoint/commit marker：已完成则返回既有结果，明确未提交才允许同一 fenced attempt 继续，结果未知进入 reconciliation，禁止重放审批、模型、Tool 或文件副作用。
- 旧同步入口 fail-closed：`POST /api/aigateway/workspace/{code}/finalize` 不得直接复制 final 文件、修改 Artifact/Workspace 或把任务标记完成；它只能在完整批准元组验证通过后幂等请求 durable queue resume。待审批、拒绝、冲突、损坏或已被其他 lease 处理时必须返回当前稳定状态/错误，不得降级调用旧 finalization service。
- 以上是最终闭环的活动契约；现有源码候选仍必须通过竞争审批/拒绝、checkpoint 损坏、commit outcome unknown、kill/restart 与幂等恢复的完整测试矩阵和生产证据，源码类或局部测试存在不等于该闭环已完成。

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
- 新增、删除或重命名错误码时，必须同步更新本文第 11 节并运行错误码目录测试。
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
- `AIARCH007` 只按完全限定 symbol identity 识别 request interface、`AuthorizeRequirementAttribute`、MVC `ControllerBase` / HTTP action attribute / `[Authorize]` / `[AllowAnonymous]`、tool descriptor 和契约例外；同名类型、伪属性、attribute alias 或换 namespace 都不得扩大识别面。Service 的公开 command/query/stream request 必须显式声明 `AuthorizeRequirement`，stream 没有例外；只有 `FinalizeCloudOidcLoginCommand`、`LoginUserCommand`、`GetCurrentUserProfileQuery`、`GetInitializationStatusQuery`、`AuditCloudOidcExternalSessionCommand` 五个完全限定 Identity 公开请求例外。其中 `AuditCloudOidcExternalSessionCommand` 只允许由服务端受控的 finalize、confirm、cancel 流程追加拒绝类审计，载荷只能包含服务端推导的原因和 profile 事实，不得携带 password、token、cookie 或原始凭据，也不得执行登录、绑定、角色/权限变更或签发 token。资源所有权/动态权限不得用不真实的单一静态权限换绿；只有 `GetArtifactWorkspaceQuery` / `DownloadArtifactQuery -> ArtifactWorkspaceQueryCoordinator` 和 `ApproveAgentApprovalCommand` / `RejectAgentApprovalCommand -> AgentApprovalDecisionCoordinator` 四个完全限定 `ResourceAuthorizationOwner` 对，并由 coordinator 执行真实 owner/approval-type/privileged permission 校验。HttpApi Controller action 必须在类或方法上显式 `[Authorize]` / `[AllowAnonymous]`。
- 上述边界由 `AICopilot.Architecture.Analyzers` 在所有生产编译中以 `Error + IsEnabledByDefault + NotConfigurable` 执行，CompilationEnd 规则保留 `CompilationEnd` tag；`AICopilot.Architecture.AnalyzerTests` 保持正/反语义 fixture 和真实临时 csproj 编译/suppression fixture，inventory 同时扫描 `NoWarn`、Analyzer 关闭、`.editorconfig/.globalconfig` severity、`#pragma warning disable`、`SuppressMessage/UnconditionalSuppressMessage`；不得恢复可降级 descriptor 或同义 Regex/字符串影子门禁。

## 9. 验收命令

以下命令只用于 Agent/异常专题的受影响范围诊断。任务完成按当前 task mode 和 selector 运行 Architecture/Security 与受影响 Business；不得自动追加全仓 required、Web、coverage、mutation、duplication 或 deployment 全量。只有用户显式授权 `Quality`、`Full` 或 `CrossProject` 时，才进入对应完整验收。

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

## 11. 前端错误码目录

本节是后端拥有的前端错误契约。前端必须为每个后端 code 提供明确用户文案。结构化 Chat Error 优先展示 `userFacingMessage`，其次是安全 `detail`，最后才使用 code fallback。

HTTP `ProblemDetails.extensions.code` 与 `traceId` 是保留键；descriptor extension 不能伪造。Plan 完整性错误只允许公开固定 detail/userFacingMessage 和合法 `taskId`，不得泄露内部错误。`AgentEvent` 使用 `stage/code/detail/recoverable/suggestedAction/metadata` 的 camelCase 字段。

### 11.1 Auth codes

| Code | 前端语义 |
|---|---|
| `account_disabled` | 账号已禁用 |
| `session_revoked` | 当前会话已撤销 |
| `user_missing` | 当前用户不存在 |
| `missing_permission` | 缺少所需权限 |
| `invalid_credentials` | 登录凭据无效 |
| `unauthorized` | 请求未认证 |
| `cloud_oidc_not_configured` | Cloud OIDC 未配置 |
| `cloud_oidc_invalid_principal` | Cloud OIDC 身份无效 |
| `cloud_identity_inactive` | 绑定的 Cloud 身份已失效 |
| `cloud_identity_unverified` | Cloud 身份未验证 |
| `external_identity_confirmation_required` | 同名本地账号需要使用本地密码确认绑定 |
| `external_identity_conflict` | 外部身份与现有绑定冲突 |
| `last_enabled_admin_required` | 该操作会移除最后一个启用管理员；不得自动重试 |

### 11.2 Application/Agent codes

| Code | 前端语义 |
|---|---|
| `request_validation_failed` | 请求在 handler 前校验失败 |
| `internal_server_error` | 全局异常边界处理的意外错误 |
| `persistence_commit_outcome_unknown` | 写入可能已提交但无法确认；不得自动重试 |
| `rate_limit_exceeded` | 超过限流 |
| `chat_context_expired` | 对话上下文已过期 |
| `chat_configuration_missing` | 对话运行配置缺失 |
| `chat_stream_failed` | 对话流失败 |
| `agent_session_reset_required` | 会话状态缺失、损坏、不兼容、超限或过期；必须新建会话 |
| `agent_session_interrupted` | 上一轮运行中断；禁止自动恢复或重放工具 |
| `agent_session_version_conflict` | 会话正在运行或乐观并发版本已变化 |
| `model_provider_unavailable` | 模型服务不可用或暂时失败 |
| `model_request_timeout` | 模型请求超时 |
| `approval_stream_failed` | 审批流失败 |
| `approval_already_processed` | 审批已处理 |
| `agent_approval_state_conflict` | 审批状态与耐久 checkpoint 冲突 |
| `agent_approval_rejected` | 用户已拒绝审批，不得自动继续 |
| `approval_pending` | 审批等待中 |
| `capability_not_allowed` | 请求能力不允许 |
| `control_action_blocked` | 控制或写操作被阻断 |
| `token_budget_exceeded` | Token 预算已用尽 |
| `onsite_presence_required` | 需要现场在场证明 |
| `onsite_presence_expired` | 现场在场证明已过期 |
| `approval_reconfirmation_required` | 需要重新确认审批 |
| `tool_not_registered` | Tool 未注册 |
| `tool_disabled` | Tool 已禁用 |
| `tool_blocked` | Tool 被策略阻断 |
| `tool_permission_denied` | 缺少 Tool 权限 |
| `tool_requires_approval` | Tool 需要审批 |
| `tool_input_invalid` | Tool 输入无效 |
| `tool_output_schema_invalid` | Tool 输出不符合封闭 schema/耐久输出契约 |
| `tool_execution_timeout` | Tool 执行超时 |
| `cloud_readonly_tool_disabled` | Cloud 只读 Tool 已禁用 |
| `cloud_readonly_intent_unsupported` | Cloud 只读意图不支持 |
| `planner_model_unavailable` | Planner 模型不可用 |
| `planner_tool_catalog_empty` | Planner Tool catalog 为空 |
| `planner_tool_schema_unsupported` | Planner Tool schema 不支持 |
| `agent_plan_invalid` | Agent Plan 无效 |
| `plan_payload_too_large` | canonical Plan v2 超过 UTF-8 字节上限，未持久化 |
| `evidence_payload_too_large` | inline canonical Evidence 超过字节上限，未接受 |
| `agent_plan_tool_denied` | Plan 请求了被拒绝的 Tool |
| `agent_plan_schema_invalid` | Plan schema 无效 |
| `tool_execution_not_found` | Tool 执行记录不存在 |
| `artifact_finalized` | Artifact 已完成，不能修改 |
| `artifact_generation_failed` | Artifact 生成失败 |
| `workspace_manifest_invalid` | Workspace manifest 无效 |
| `agent_task_run_in_progress` | AgentTask 已在运行 |
| `agent_task_retry_not_allowed` | 当前任务不允许重试 |
| `agent_task_run_lease_expired` | 任务 lease 已过期 |
| `agent_task_cancellation_requested` | 已请求取消任务 |
| `agent_task_run_queued` | 任务已入队 |
| `agent_task_run_queue_not_found` | 队列项不存在 |
| `agent_task_run_queue_lease_expired` | 队列 lease 已过期 |
| `agent_task_run_fence_stale` | 任务 fencing token 过期，worker 必须停止写入 |
| `agent_node_run_fence_stale` | 节点 fencing token 过期，必须对账 |
| `agent_node_run_state_conflict` | 节点状态迁移冲突 |
| `agent_run_budget_exceeded` | 封存预算已超限 |
| `agent_worker_unavailable` | Agent worker 不可用 |
| `agent_worker_workspace_mismatch` | Worker 与 HttpApi workspace 不一致 |
| `agent_finalization_state_conflict` | 最终审批或耐久完成状态冲突 |
| `agent_run_queue_dead_letter_not_allowed` | 不允许 dead-letter 操作 |
| `agent_run_queue_operation_denied` | 队列操作被拒绝 |

### 11.3 Cloud AiRead codes

| Code | 前端语义 |
|---|---|
| `cloud_ai_read_not_configured` | Cloud AiRead 未配置 |
| `cloud_ai_read_request_blocked` | 请求被 endpoint policy 阻断 |
| `cloud_ai_read_invalid_request` | 参数超出正式 endpoint 契约 |
| `cloud_ai_read_unauthorized` | Cloud AiRead 未认证 |
| `cloud_ai_read_forbidden` | Cloud AiRead 无权限 |
| `cloud_ai_read_not_found` | Cloud 资源不存在 |
| `cloud_ai_read_rate_limited` | Cloud AiRead 被限流 |
| `cloud_ai_read_unavailable` | Cloud AiRead 不可用 |
| `cloud_ai_read_missing_required_parameter` | 缺少正式接口必填参数 |

Cloud typed GET 的路径、参数、结果 envelope、设备解析和 no-fallback 规则只在[Cloud 只读数据分析契约](./Cloud只读数据分析契约.md)维护，本节不复制第二份接口表。
