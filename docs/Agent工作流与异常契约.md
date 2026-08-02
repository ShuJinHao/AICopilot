# Agent 工作流与异常契约

本文档是 AICopilot 主聊天运行时、工具、批准、异常和前端展示的活动契约。主聊天只有 Microsoft Agent Framework Harness 一条运行主链；历史实现只通过 Git 追溯，未完成方向见 `docs/AI架构路线图.md`。

## 1. 唯一运行主链

- `POST /api/aigateway/chat` 与 `POST /api/aigateway/approval/decision` 只能构造并续流同一个 `HarnessAgent`。`ChatStreamHandler`、`ApprovalDecisionStreamHandler` 不得保留第二套路由、计划编译、fan-out/fan-in 或 worker 执行入口。
- 前端只有一个聊天输入框。用户输入无论当前是 `Plan` 还是 `Execute` 都进入 Harness；不得显示或调用已退出的任务面板、业务审批、文件工作台或聊天附件。
- 对话运行时公开入口只保留 Session、消息历史、Chat、Agent mode 与 Harness tool approval。`/agent/task/**`、`/agent/approval/**`、`/workspace/**`、`/artifact/**`、`/upload/**`、`/approval-policy/**`、`session/timeline` 和 `session/safety-attestation` 当前必须不可达。
- Harness 主聊天固定使用 `Microsoft.Agents.AI.Harness` / `Microsoft.Agents.AI` `1.16.0`，每轮最多 8 次模型调用。必须关闭 FileMemory、WebSearch、AgentSkills、BackgroundAgents、LoopEvaluators 与 compaction，不注册 FileAccessStore、Shell 或文件 Artifact；聊天只允许文本和服务端可信 inline Widget。
- 模型端点、认证、配额、熔断和遥测由轻量 `IChatClient` 工厂负责，且作用于每一次真实模型调用。Text-to-SQL、分类和结构化生成直接使用轻量客户端，禁止嵌套 Harness；主聊天不得恢复 `Microsoft.Agents.AI.Workflows` 依赖。
- `ToolSurfaceGuardChatClient` 是模型请求与模型工具调用的最内层边界：请求侧按服务端真实模式过滤工具并始终删除 `mode_set`；响应侧对未公开工具、伪造批准响应和模式不允许的调用 fail-closed。该边界不能依赖提示词。

### 1.1 Plan / Execute

- `Plan` 与 `Execute` 是同一 Harness `AgentSession` 的服务端权威模式；新会话默认 `Plan`，不得映射为另一套任务状态机。
- `Plan` 只允许 Harness Todo 与 `mode_get`，不得向模型公开 Cloud、RAG、MCP、`BusinessQuery`、`KnowledgeQuery` 或其它外部/业务工具，也不得执行真实业务动作。
- `Execute` 可以直接回答，或调用经过当前用户、当前会话和实时安全门禁筛选的工具。模式切换不能扩大注册表、权限、风险、批准或数据边界。
- 模式切换唯一入口是认证的 `PUT /api/aigateway/session/{sessionId}/agent-mode`，请求携带 `plan|execute` 与 `expectedVersion`，仅 owner 可调用，并通过 Harness 官方 `SetModeAsync` 修改。活跃 turn、待批准、Interrupted 或版本冲突必须拒绝。

### 1.2 AgentSession 持久化

- 每个新 Session 同步创建一条 `agent_session_states` 一对一记录，绑定 `session_id + user_id + tenant_id`，保存 schema version、受保护的完整 AgentSession、`Ready|Running|Interrupted`、active turn、乐观并发版本和创建/更新/过期时间。
- AgentSession 明文上限 2 MiB，采用 30 天滑动 TTL。运行前在会话锁内写 `Running + turn id`；每次真实模型调用后 checkpoint。正常完成、等待批准和模式切换都要保存完整状态。
- 取得锁后发现遗留 `Running` 时只允许转为 `Interrupted`，不得自动恢复、重放模型或重放工具。状态缺失、schema 不匹配、损坏、超限或过期统一返回 `agent_session_reset_required` 并要求新建会话。
- 状态使用 ASP.NET Core Data Protection 固定用途字符串认证加密，`ApplicationName` 固定为 `AICopilot.AgentSessions`。生产 key ring 固定持久化到 `/var/lib/aicopilot/data-protection-keys`；目录必须预先存在、归属运行用户、owner 可读写执行，且 group/other 不可写。密钥、状态明文和解密错误不得写入日志。
- 当前部署拓扑只支持 `SingleInstance`。HTTP API 启动时必须拒绝其它拓扑、错误路径、符号链接、错误 owner 或不安全权限；多实例必须先另立共享 key provider 契约，不得复用本地目录假装共享。

## 2. 模型可见工具

### 2.1 统一注册表门禁

- 本地插件与 MCP 工具必须统一经过 `MainChatToolGate`。缺少精确注册、登记与运行时身份不一致或任一检查失败时，工具不得进入模型目录。
- 门禁逐项校验 `IsEnabled`、`IsExecutableByAgent`、当前用户 `RequiredPermission`、`RiskLevel`、`RequiresApproval`、`AuditLevel`、`DataBoundary`、`SchemaVersion`、输入/输出 schema 与 `AiToolSafetyPolicy`。
- 运行时不得用工具名 alias、描述、endpoint、hostname 或调用方自报值替代规范身份和治理元数据。动态 MCP 只有 server 与 tool 都满足 `CloudReadOnly + ReadOnlyQuery + readOnlyDeclared=true`，并携带独立 canonical `ToolName` 时才可继续检查。
- MCP 运行时固定为稳定版 `ModelContextProtocol 2.0.0` typed API。HTTP 保留存量 `Sse` 配置值但内部执行 discovery-first `AutoDetect` 并由 SDK 回退旧握手；Stdio 禁止继承任意父进程环境，只传递 SDK 安全默认集，stderr 不记录原文。
- MCP discovery 的 canonical identity 固定为 `serverName + ProtocolTool.Name`。`inputSchema` / `outputSchema` / typed annotation 任一缺失、非法或与本地治理元数据冲突时，登记标记不可执行且运行时工具撤下。每轮 refresh 必须同时比较数据库 `RowVersion` 与远端 schema/hint、有效权限/审批/审计/数据边界/schema version 组成的指纹；删除、漂移或 discovery 失败不得保留旧插件。
- MCP 调用前再次执行同一 `AiToolSafetyPolicy` 并验证 canonical 参数；调用后只允许通过本地封闭 output schema 的 bounded structured content 进入模型。保留 scalar、array、object 等原生 JSON 类型，不生成 `{ result: ... }` 兼容包装；文本替代、未知 shape 和远端 error 全部 fail-closed。
- `DiagnosticAdvisorPlugin/GenerateDiagnosticChecklist` 必须有唯一、精确、版本化登记；不得依赖宽泛插件放行。
- 主 Harness 必须设置 `DisableToolAutoApproval=true` 与 `ChatOptions.AllowMultipleToolCalls=false`，且不得配置 `ToolApprovalAgentOptions` / `AutoApprovalRules`。需要批准的工具继续使用官方 `ApprovalRequiredAIFunction` 逐次询问；关闭的是支持 standing rules 和多批准排队的 `ToolApprovalAgent` 中间件。禁止“不再询问”及 `AlwaysApproveToolApprovalResponseContent` 等永久批准信号。单工具限制只属于主聊天；Text-to-SQL、分类和结构化生成等轻量内部 Agent 保持各自现行调用策略。

### 2.2 BusinessQuery

- `BusinessQuery` 是服务端固定 Execute-only 工具，先校验 `AiGateway.Chat` 权限；Text-to-SQL 只可作为内部 fallback，绝不直接暴露给模型。
- Cloud typed provider 是首选路径。只有同一 Cloud profile 返回 `Unsupported` 或 `Unavailable` 才可进入受控 Text-to-SQL；`Success`、`Empty`、`NeedClarification`、`Unauthorized` 均不得 fallback。禁止跨源、Simulation、MCP 或隐藏 adapter fallback。
- `Analysis.Recipe.*` 具体配方数据在语义规划器、provider、数据库和 fallback 之前即被拒绝。SQL 最终统一经过 profile-aware AST 只读门禁和只读数据库账号。
- 模型只接收 bounded、脱敏的业务摘要和治理证据，不得接收 SQL、原始行、连接信息、内部 schema、provider raw output 或未授权字段。

Cloud 只读正式能力为：

- `Analysis.Device.List/Detail/Status`
- `Analysis.DeviceLog.Latest/Range/ByLevel`
- `Analysis.Capacity.Range/ByDevice`
- `Analysis.ProductionData.Latest/Range/ByDevice`
- `Analysis.Process.List/Detail`
- `Analysis.ClientRelease.List`

### 2.3 KnowledgeQuery

- `KnowledgeQuery(question, knowledgeBaseNames)` 是服务端固定 Execute-only 工具，同时校验 `AiGateway.Chat` 与 `Rag.SearchKnowledgeBase`；Plan 模式绝不公开。
- 授权目录只能由服务端按当前用户实时生成。恰好一个授权知识库时可自动选择；存在多个知识库时，调用必须给出服务端授权目录中的精确名称。
- 每个知识库最多返回 3 条，总计最多 12 条。模型只接收脱敏摘要、引用和治理证据，不得接收凭据、控制文本、原始向量结果或未授权内容。
- 空问题、未知名称、无权名称、歧义名称、超出上限或检索失败必须 fail-closed，不能退化为跨库搜索、默认全选或暴露知识库存在性差异。

### 2.4 可信 Widget

- Widget 只能由业务查询服务端代码生成 `ChunkType.Widget`，写入 turn-scoped `TrustedRenderChunkBuffer`，再作为独立 SSE chunk 发送。模型文本与模型工具输出都不能生成、嵌入或伪造 Widget JSON。
- 只允许既有受支持 Widget 类型，并沿用统一序列化、字段脱敏和 UTF-8 大小上限。未知类型、超限、无图表、空结果、失败或取消时均不得发送 Widget。
- 模型看到的是与 Widget 分离的安全摘要；前端 reducer 只接受显式 `ChunkType.Widget`，不得从普通 Text chunk 猜测或解析 Widget。

## 3. Harness tool approval

- Tool 从注册表到 Harness wrapper 必须完整保留规范身份、`RequiresApproval`、风险、权限、审计、数据边界、schema version 和 canonical 参数摘要。
- 每次批准绑定用户、租户、session、request id、toolCallId、规范工具身份、schema version 与 canonical 参数 SHA-256。续流前重新验证全部绑定；漂移、跨 owner/session、重复或外来 call、Interrupted 后旧批准全部 fail-closed。
- 本版批准协议每个 governed turn 只允许一个不同的工具调用。provider 即使忽略 `AllowMultipleToolCalls=false` 返回第二个不同的待批调用，也不得执行任一工具、不得续批或保存部分绑定；服务端必须清空批准绑定，将 AgentSession 标记为 `Interrupted`，返回既有 `agent_session_interrupted` 并要求用户新建会话。
- 主聊天只允许批准 AICopilot 自身、可逆、幂等或结果可查询的动作。Cloud/MES/ERP 写入、设备启停、生产控制、不可逆或结果不可查询动作即使用户批准也继续硬阻断。
- 权威入口只有 `GET /api/aigateway/approval/pending` 与 `POST /api/aigateway/approval/decision`。业务审批与策略管理不属于 Harness approval。

## 4. Cloud 写入禁止

- Harness、MCP、Tool、后台任务、直接 SQL 和隐藏 adapter 均不得创建、修改、删除、补录、审批、派发或触发 Cloud 业务数据。
- Human-in-the-loop 不是 Cloud 业务写入授权。
- 未来若需要 Cloud AI-facing 写接口，必须由用户明确批准新的跨仓库接口、权限、审计和回滚契约；不得在 AICopilot 内部先行实现。

## 5. 异常、日志与持久化

- 未知 HTTP 异常走稳定 ProblemDetails，返回 canonical `code`、安全 `detail`、`userFacingMessage` 和当前 `traceId`。descriptor extensions 中大小写变体的 `code` / `traceId` 必须丢弃，调用方不能注入伪值。
- SSE Error / AgentEvent 也只返回稳定错误码和安全摘要；字段固定为 camelCase。Chat Error 的 `code`、`detail`、`userFacingMessage` 均是后端完成脱敏后的活动诊断契约，前端必须完整展示；未知 code 只在缺少用户提示时使用固定 fallback，不得因此隐藏安全 `detail`。后端不得把 raw exception、SQL、内部路径或 provider 原文放入这些字段。
- `persistence_commit_outcome_unknown` 表示写入可能已提交，调用方不得自动重试；必须先按非敏感 commit id 对账。commit marker、advisory lease 和文件 reconciliation 规则仍由持久化专题契约负责。
- 日志、审计和持久化失败原因只允许 trace/correlation id、exception type、稳定 code、长度、计数和 SHA-256。不得记录 raw exception message/object、SQL、prompt、参数值、token、密码、连接串、endpoint、source/table/database、原始工具参数或结果行。
- AgentSession 状态、Data Protection key、Knowledge 内容和工具 raw output 视为敏感数据；任何失败路径都不得为了诊断将其写入日志。

## 6. 前端展示

- 普通 API、SSE、AgentEvent、Harness ApprovalRequest、Chat Error、OIDC、auth、RAG、Config 与 route guard 的失败必须进入可见错误栏、dialog 或安全 fallback，不得只写 console 或空 catch。
- 前端错误栏完整展示后端 `code`、`userFacingMessage`、validation errors、安全 `detail` 与 `title`；未知 code 在缺少用户提示时使用固定 fallback，但仍保留后端安全诊断字段。
- 新会话默认 `Plan`。空态建议必须随当前模式变化：`Plan` 只能建议规划步骤和待办，`Execute` 才能建议设备日志、状态、工序、版本等真实只读查询；点击建议不得自动切换模式。
- `Running`、等待批准、`Completed`、`Failed`、`Interrupted`、`ResetRequired` 必须由真实 Session、stream、pending approval 和 error 状态统一投影。`Interrupted` / `ResetRequired` 禁用发送、模式切换和批准，并只提供“新建会话”主操作，不得恢复或自动重放。
- 批准卡只展示服务端返回的规范工具身份和白名单安全参数摘要；不得展开原始参数，不得提供 standing rule 或“不再询问”。提交批准或拒绝时按钮立即锁定，刷新后只以 `/api/aigateway/approval/pending` 恢复权威状态。
- 运行详情默认折叠，只展示工具名、查询次数、返回行数、截断状态、Widget 类型、业务过滤条件和安全摘要；不得展示 SQL、连接信息、内部路径、原始结果行或未脱敏错误。
- 消息、SSE、运行状态、Harness approval、inline Widget 与 Interrupted / ResetRequired 新建会话提示均以服务端状态为权威。
- 对话在 `1920×1080`、`1366×768` 与 `1024×768` 必须保持消息区、工具栏和固定输入区不重叠；窄屏会话栏使用抽屉，交互按钮命中区域不得小于 40px，light/dark 均使用活动设计 token。

## 7. 物理边界与禁止回潮

- 当前二进制、DI、HTTP 路由、数据库 baseline 和前端不得包含另一套计划编译、意图分支、耐久任务队列、业务审批、文件工作台、路由模型或时间线投影。
- AiGateway 文件上传、报告/PDF/PPTX/XLSX 生成、预览和下载不属于主聊天。RAG 文档上传与可信 inline Widget 保持独立活动契约。
- 不得为兼容旧客户端、旧测试或旧数据恢复已退出路由、类型、配置、权限、错误码、表或种子。历史只通过 Git 和本批保留的恢复 bundle 追溯。

## 8. 源码归属与门禁

- Harness 主链：`src/services/AICopilot.AiGatewayService/Agents`。
- 工具注册、安全与 MCP：`src/services/AICopilot.AiGatewayService/Tools`、`src/services/AICopilot.McpService`、`src/infrastructure/AICopilot.Infrastructure/Mcp`。
- BusinessQuery 与可信 Widget 适配：`src/services/AICopilot.AiGatewayService/BusinessQueries` 与 `src/services/AICopilot.AiGatewayService/Agents/MainChatBusinessQueryTool.cs`。
- AgentSession 加密持久化：`src/infrastructure/AICopilot.EntityFrameworkCore`；启动目录验证：`src/hosts/AICopilot.HttpApi`。
- 后端异常：`src/hosts/AICopilot.HttpApi/Infrastructure/UseCaseExceptionHandler.cs` 与 `src/shared/AICopilot.SharedKernel/Result`。
- 前端 SSE、错误、runtime details 与 Widget：`src/vues/AICopilot.Web/src/protocol`、`src/vues/AICopilot.Web/src/stores`、`src/vues/AICopilot.Web/src/components/chat`。
- Architecture Analyzer 继续以不可降级 Error 执行。任何受影响的 .NET filtered test 必须先用完全相同 filter 的 `--list-tests` 证明命中；默认不追加全量、coverage、mutation、duplication、Quality、CrossProject、数据库应用或部署。

API 未处理异常策略发生变化时，定向诊断必须使用实际拥有该测试的 InProcess 项目，并按相同 filter 先列举再执行：

```bash
dotnet test src/tests/AICopilot.InProcessTests/AICopilot.InProcessTests.csproj --filter "UnhandledApiExceptionPolicyTests" --no-restore --list-tests
dotnet test src/tests/AICopilot.InProcessTests/AICopilot.InProcessTests.csproj --filter "UnhandledApiExceptionPolicyTests" --no-restore
```

## 9. 后端错误码目录

前端必须为以下活动 HTTP / SSE code 提供明确、安全的用户文案；新增、删除或重命名后端错误码时同步更新本节和对应目录测试。

| Code | 前端语义 |
|---|---|
| `account_disabled` | 账号已禁用 |
| `session_revoked` | 当前会话已撤销 |
| `user_missing` | 当前用户不存在 |
| `unauthorized` | 请求未认证 |
| `missing_permission` | 缺少所需权限 |
| `invalid_credentials` | 登录凭据无效 |
| `cloud_oidc_not_configured` | Cloud OIDC 未配置 |
| `cloud_oidc_invalid_principal` | Cloud OIDC 身份无效 |
| `cloud_identity_inactive` | Cloud 身份未启用 |
| `cloud_identity_unverified` | Cloud 身份未验证 |
| `external_identity_confirmation_required` | 外部身份绑定需要确认 |
| `external_identity_conflict` | 外部身份绑定冲突 |
| `last_enabled_admin_required` | 必须保留至少一个启用的管理员 |
| `request_validation_failed` | 请求校验失败 |
| `internal_server_error` | 意外错误 |
| `persistence_commit_outcome_unknown` | 写入结果未知，禁止自动重试 |
| `rate_limit_exceeded` | 超过限流 |
| `chat_context_expired` | 对话上下文已过期 |
| `chat_configuration_missing` | 对话运行配置缺失 |
| `chat_stream_failed` | 对话流失败 |
| `agent_session_reset_required` | 会话状态不可恢复，必须新建会话 |
| `agent_session_interrupted` | 上一轮中断，禁止自动恢复或重放 |
| `agent_session_version_conflict` | 会话正在运行或版本已变化 |
| `model_provider_unavailable` | 模型服务不可用 |
| `model_request_timeout` | 模型请求超时 |
| `approval_stream_failed` | Harness 批准续流失败 |
| `approval_already_processed` | 批准已处理 |
| `approval_pending` | 正在等待逐次批准 |
| `capability_not_allowed` | 请求能力不允许 |
| `control_action_blocked` | 控制或写操作被硬阻断 |
| `token_budget_exceeded` | 模型调用预算已用尽 |
| `tool_not_registered` | Tool 未精确注册 |
| `tool_disabled` | Tool 已禁用 |
| `tool_blocked` | Tool 被安全策略阻断 |
| `tool_permission_denied` | 缺少 Tool 权限 |
| `tool_requires_approval` | Tool 需要逐次批准 |
| `tool_input_invalid` | Tool 输入或 schema 无效 |
| `tool_output_schema_invalid` | Tool 输出不符合封闭 schema |
| `tool_execution_timeout` | Tool 执行超时 |
| `cloud_readonly_tool_disabled` | Cloud 只读 Tool 已禁用 |
| `cloud_readonly_intent_unsupported` | Cloud 只读意图不支持 |
| `cloud_ai_read_not_configured` | Cloud AiRead 未配置 |
| `cloud_ai_read_request_blocked` | Cloud AiRead 请求被阻断 |
| `cloud_ai_read_invalid_request` | Cloud AiRead 参数无效 |
| `cloud_ai_read_unauthorized` | Cloud AiRead 未认证 |
| `cloud_ai_read_forbidden` | Cloud AiRead 无权限 |
| `cloud_ai_read_not_found` | Cloud 资源不存在 |
| `cloud_ai_read_rate_limited` | Cloud AiRead 被限流 |
| `cloud_ai_read_unavailable` | Cloud AiRead 不可用 |
| `cloud_ai_read_missing_required_parameter` | Cloud AiRead 缺少必填参数 |

Cloud typed GET 的路径、参数、结果 envelope 与 no-fallback 规则只在[Cloud 只读数据分析契约](./Cloud只读数据分析契约.md)维护，本文件不复制接口表。
