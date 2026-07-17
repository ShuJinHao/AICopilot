# AICopilot Instructions

修改 `AICopilot` 前先读：

- 工作区总规则：`../docs/总规则.md`
- AI 业务规则：`资料/AICopilot业务规则.md`
- 当前任务相关的专题契约、源码和测试。

`docs/改动复盘与规则沉淀.md`、`../docs/历史核心记录.md`和旧计划默认不全文读取。只在修复历史回归、修改已冻结链路、当前实现与契约冲突、测试失败无法从源码/契约定位、同类问题曾发生或用户明确要求追溯时，按模块名、Rule ID、错误码或关键类型定向检索；历史记录不得代替当前正式规则。

## Positioning

`AICopilot` 是 AI 助手和受控编排系统，不是云端仓库，不是客户端仓库，也不是制造业务主数据来源。

默认只修改 `AICopilot`。修改 Cloud 或 Edge 必须由用户在当前轮明确授权。

## Change Closure

- 修改 AICopilot 代码前，必须先读工作区总规则、本文档、`资料/AICopilot业务规则.md`、相关专题契约、相关源码、相关测试和近期 git/GitHub 历史。
- 已验收功能默认冻结；不能因为局部重构、测试修复或文档整理顺手改变已有 AI 工作流、Cloud 只读边界、审批边界、工具边界或前端错误契约。
- 每次代码改动完成前，必须更新项目滚动复盘文档 `docs/改动复盘与规则沉淀.md`，最新记录放在最前。
- 每次复盘必须写明改动范围、改动原因、影响面、验证命令、验证结果和规则提炼结论。
- 必须判断是否形成长期规则；有则写入本文档、`资料/AICopilot业务规则.md`、专题契约或工作区长期规则，无则在复盘中写明“无新增长期规则”及原因。
- 新增、删除或重命名后端 `AuthProblemCodes`、`AppProblemCodes`、`CloudAiReadProblemCodes` 时，必须同批更新 `docs/frontend-integration-contract-package-2026-05-17.md`，并运行 `ErrorCodeCatalogTests`。
- 大范围架构、管道、权限、工作流或契约改动不得只以 filtered tests 作为完成依据；`Get-AICopilotTestInventory.ps1` 发现的全部 required 物理 runner 未执行/未完成发现数与执行数对账、CI 全量未确认或环境依赖失败时，最终回复和复盘必须明确标注。
- 最终回复必须列出复盘文档、规则沉淀位置和验证命令；缺任一项，不得称为完成。
- 默认只更新项目滚动复盘文档，不为每个任务新增单独流水文档；只有形成可长期复用的业务或技术契约，才新增专题文档。

## Topic Contracts

- AI 修复总计划和逐项治理清单：`docs/AI架构治理清单.md`。
- 部署安全、HTTP-only、secret、镜像、SSH 和 runner 边界：`docs/AICopilot安全部署契约.md`。
- Cloud 只读、Cloud AiRead、CloudReadOnly Direct DB 和 Text-to-SQL 边界：`docs/Cloud只读数据分析契约.md`。
- Agent workflow、Plan/Chat、MCP/Tool/Human-in-the-loop、异常和前端错误边界：`docs/Agent工作流与异常契约.md`。

触碰对应能力时必须先读相应专题契约；专题契约与本文档冲突时，先停止并修正文档口径，不得按印象继续改代码。

## Cloud Business Read-only Boundary

- AICopilot 可以读取已批准范围内的 Cloud 业务数据，用于分析、解释、汇总、检索和建议。
- AICopilot 不得注册、修改、删除、补录、审批、派发或触发 Cloud 业务数据和业务流程。
- AICopilot 不得直接写 Cloud 数据库。
- AICopilot 不得通过 MCP、Tool、Agent workflow、后台任务、直接 SQL 或隐藏适配器间接调用 Cloud 写接口。
- Human-in-the-loop 不是放开 Cloud 写入的理由。
- AICopilot must not create, update, delete, backfill, approve, dispatch, or trigger Cloud business records.
- AICopilot must not directly write to the Cloud database.
- Human-in-the-loop approval is not permission to write Cloud business data.
- Cloud AI-facing APIs are read-only contract surfaces unless the user explicitly approves a new cross-repository write contract.
- Cloud AiRead 正式设备参数是 `deviceId`；`deviceCode` 只能用于设备查询/解析，不得被当作 `deviceId` 发送给 Cloud。
- AICopilot `CloudAiReadEndpointPolicy` 和 `ICloudAiReadClient` 必须逐项覆盖 Cloud `AI只读接口契约.md` 已批准的正式 `GET /api/v1/ai/read/*` 表面；不能只接高频端点后宣称 Cloud-AI 接口已全部对齐。
- Cloud AiRead 客户端只允许八个正式 typed GET，不得暴露任意 method/path 传输、可配置 POST allowlist、legacy adapter 或双轨接口；`production-records` 正式工序/工位字段缺失时必须保持空，不得用 `typeName` 或 `typeKey` 代填。
- Cloud 只读读取只能向 Cloud 发送真实端点参数；`scenarioId`、`from`、`to`、`pilotWindowId`、`boundary` 等试点元数据不得透传给 Cloud。
- 内部开发允许通过 DataAnalysis `CloudReadOnly` 只读数据源直连真实 Cloud PostgreSQL 做 Text-to-SQL 验证；必须使用已验证只读数据库账号、白名单表字段和只读 SQL guard，不得写 Cloud 业务数据，也不得用 Simulation 冒充真实数据。
- `Device`、`DeviceLog`、`Capacity`、`ProductionData`、`Process`、`ClientRelease` 六类已覆盖正式 `Analysis.*` 语义必须只走 Cloud AiRead 八个 typed GET；Cloud 关闭、空集、400/401/403/429/5xx、timeout 或非法 JSON 均不得回退 Direct DB、Text-to-SQL、Simulation、MCP 或隐藏适配器。
- Cloud tool 安全元数据只有 `boundary=CloudReadOnly + capability=ReadOnlyQuery + readOnlyDeclared=true` 这一精确组合有效；`Diagnostics`、`LocalSuggestion`、`SideEffecting`、缺失值、同名伪符号或无法静态证明的声明都必须 fail-closed。动态配置的 MCP 目标无法仅凭调用方 enum、alias、描述或 endpoint 证明为可信 NonCloud，因此 server 和 tool 都必须使用上述精确只读组合，并在聚合注册、runtime builder（含旧持久化记录）、Plan 能力发现和每次 `McpAgentToolExecutor` 执行时复用同一 `AiToolSafetyPolicy`。runtime MCP tool 还必须显式携带独立 canonical `ToolName`，禁止回退到 runtime `Name`、日志文案或其它 alias。本地非 MCP tool 按其正式策略和审批边界处理，不得用 MCP 的 fail-closed 分类伪装删除必要的本地副作用能力。
- CloudReadOnly Direct DB / Text-to-SQL 只服务上述正式语义覆盖外的低频自由探索和治理白名单内补充分析；探索型 Text-to-SQL 只能在这类未覆盖问题或其 Direct DB SQL 失败后受控 fallback，不能执行、拆分、重命名或旁路任何已覆盖 `Analysis.*` intent。
- CloudReadOnly Text-to-SQL fallback 必须同时满足已验证只读凭据、共享白名单 schema、LLM 结构化生成、SELECT-only SQL guard、BusinessQuery safety policy 双层表/列白名单、只读执行和 hash-only 审计；生产 fallback 不得退化为规则 SQL 模板。
- CloudReadOnly Text-to-SQL LLM prompt 可见的物理 schema 仅限 `CloudReadOnlyGovernedSchema` 批准的表名、列名、列类型、join hints 和必要业务描述；不得暴露连接串、凭据、role/权限细节、样例数据、查询结果、参数值、非白名单表字段或系统/敏感字段。
- CloudReadOnly Text-to-SQL 修复重试默认最多 3 次、硬上限 5 次；timeout、权限、凭据、非只读、系统表、敏感字段、多语句或写 SQL 默认不可修复、不重试。
- CloudReadOnly Text-to-SQL 修复历史不得保存完整 SQL、用户 prompt、连接串、参数值或敏感字段；上一轮失败 SQL 只允许在当前调用内以内存参数临时回传给 LLM 生成下一版，不能写入审计、日志、state、结果或持久化对象。
- Legacy `DataSourceSelectionMode.TextToSql` / Business Text-to-SQL draft runtime 仍只服务 SimulationBusiness；Agent 侧 CloudReadOnly 查询只能走 governed fallback runner，不能复用会生成 SimulationBusiness SQL 的旧 draft runtime。
- Direct DB 语义映射如需展示工序名，只能通过只读 join `mfg_processes.process_name`；新增 join 表必须同步更新 `CloudReadOnlyGovernedSchema` 表/列/类型/join hint、SQL guard 白名单、BusinessQuery safety schema、只读 role 授权 SQL、授权探针、部署 preflight、RealSource 模板、架构测试和部署文档。
- DeviceLog 语义查询的日志级别必须使用 Cloud PostgreSQL 真实枚举值 `ERROR`、`WARN`、`INFO`；“错误+警告/异常分析”必须生成多级别只读查询，不能只查单一 `ERROR` 后推断整体无异常。
- DeviceLog 查询涉及工序或设备自然语言范围时，必须走只读 `devices` / `mfg_processes` join 暴露的业务字段过滤；不得只靠最终回答模型从文本中猜测范围。
- DataAnalysis 追问其他日志级别、工序、设备或时间窗口时，必须在路由/执行层重新生成并执行 `Analysis.DeviceLog.*` 意图；Final Agent 只能总结本轮 `query_execution` 证据，不能基于上一轮回答文本推断未查询级别“有/没有”。
- DataAnalysis 最终上下文必须携带本轮查询执行事实，包括语义 target/kind、filters、timeRange、limit、returnedRowCount 和 evidence boundary；最终回答必须先核对这些事实再下结论。
- Cloud PostgreSQL readonly role 授权的权威载体是 `deploy/enterprise-ai/cloud-readonly/apply-readonly-grants.sql` 和 `check-readonly-grants.sql`；生产只允许对治理白名单表做显式表级 `GRANT SELECT`，不得使用 `GRANT SELECT ON ALL TABLES`、默认权限、未来表自动授权或列级/表级混用口径。
- CloudReadOnly 直连数据库启用时，服务器 `deploy-release.sh` 必须先运行 `scripts/check-cloud-readonly-grants.sh` 做 fail-fast preflight；权限错误对用户可见诊断只能暴露治理白名单表名，不得输出连接串、role、密码、SQL 原文或非白名单对象。
- `Analysis.Device.List/Detail` 只表达 `/devices` 的设备主数据；`Analysis.Device.Status` 只表达 `/device-client-states` 的 Cloud 权威 `softwareStatus`、运行心跳原值和唯一 freshness 时间。无心跳设备返回 `MissingRuntimeHeartbeat` 行；仅 `asOfUtc - lastRuntimeHeartbeatAtUtc > 24h` 为 `RuntimeHeartbeatStale`，恰好 24 小时不 stale；Stale 不得翻译为 Offline/Stopped。状态空集只表示授权范围内无匹配设备，最新日志级别只属于 `Analysis.DeviceLog.*`。
- `Analysis.Process.List/Detail` 只能读取 Cloud `/processes`：支持 `processId` 精确过滤以及 `keyword/processCode/processName` 搜索；详情必须在未截断结果中唯一精确命中，`processId` 必须作为正式 GUID 参数发送、不得冒充 keyword，也不得回退其他数据源。
- `Analysis.ClientRelease.List` 只能读取 Cloud `/client-releases`，只允许 `channel/targetRuntime/status/includeArchived` 正式过滤条件；版本、hash、下载地址、发布说明和发布状态只能来自 Cloud 返回，不得由模型补写，也不得回退 Direct DB、Text-to-SQL 或 Simulation。
- Simulation 只允许作为显式离线演示/测试资产，默认关闭；Real Cloud 查询为空、失败或未配置时不得 fallback 到 Simulation 冒充真实数据。
- AICopilot 自动化如需创建或轮换 Cloud PostgreSQL 只读账号，只能创建/更新专用 readonly role，只能授予已批准白名单表 SELECT，必须要求显式确认词；不得授予写权限、schema create 权限、superuser、createdb、createrole 或 replication。
- 开发阶段已物理删除 Trial/Pilot/Production Readiness 运营线；不得把旧试点运营能力重新接回普通产品导航、Skill 或后台接口。

## OIDC Boundary

Cloud-AICopilot OIDC 身份对齐的长期结论见 `../docs/历史核心记录.md`：

- Cloud 只证明身份、账号有效性、员工有效性。
- AICopilot 保留本地 AI 用户、AI 角色、AI 权限、SecurityStamp、本地禁用、审计和 emergency admin。
- Cloud role 不直接映射 AI role。
- AICopilot 不读取 Cloud Cookie、不接收 Cloud 密码、不直连 Cloud 用户表。
- `IIoT.EdgeClient` 不参与该身份对齐，除非用户后续单独授权。

## Architecture

- 遵守 DDD 分层和依赖倒置。
- 聚合根边界以 `docs/DDD聚合根边界.md` 为长期技术契约；新增或调整 `IAggregateRoot<>`、`IRepository<T>` 注册、EF `DbSet`、Agent runtime timeline/queue/audit 记录前必须先核对该文档和架构测试。
- `IAggregateRoot<>` 只用于能独立维护业务不变量和生命周期的领域根；队列项、timeline 投影、工具执行审计、worker 心跳、执行尝试记录不得作为新增聚合根方向，历史债务只能减少不能增加。
- `DataSourcePermissionGrant` 当前保留为独立聚合根但标记待评估；后续如果能完全归入 `BusinessDatabase` 生命周期，应下沉为子实体或专用权限记录并移出白名单。
- `src/core` 放领域核心。
- `src/services` 放命令、查询、workflow、应用编排、MediatR handler。
- `src/infrastructure` 放 EF Core、Dapper、embedding、event bus、provider、MCP 技术细节。
- `src/hosts` 保持薄，只做组合根、API、worker、启动 wiring。
- `src/shared` 只放真正共享的抽象和 shared kernel。
- `src/vues` 放前端逻辑，不回填到 service 或 host。
- 修改 AICopilot 前端前必须先读 `src/vues/AICopilot.Web/AGENTS.md`，遵守前端错误契约、会话状态和 UI 状态规则。
- 不为旧 Cloud 读取路径、旧工具 schema、旧配置模式或旧文档入口保留兼容 adapter；需要跨仓库对齐时同步更新契约、服务注册和测试。
- HTTP API Controller 必须默认要求授权，或对有意公开的端点显式标注匿名访问；不能依赖“忘记加 `[Authorize]`”形成公开接口。
- MediatR `IStreamRequest` 与普通 `IRequest` 是两条管道；所有公开 stream 请求必须声明 `AuthorizeRequirement`，后续接入 `IStreamPipelineBehavior` 时不得把事务或审计边界套到 SSE/stream 长连接上。
- MediatR 横切行为只能通过 `AddAICopilotMediatRPipeline` 统一注册；service 模块只注册本模块 handler 和业务服务，不能分散注册 authorization、validation、telemetry、transaction 或 audit behavior。
- 使用 MediatR handler 的 host 组合根必须先调用统一管道入口，再注册 service 模块；worker host 不能因为挂统一管道而强依赖登录用户态。
- Stream 细粒度权限必须由 `AuthorizationStreamBehavior` 在进入 stream handler 前校验；stream behavior 只能逐项透传 `IAsyncEnumerable`，不得预读、缓存或把 SSE/stream 转成一次性结果。
- 新增或接线 `IStreamPipelineBehavior` 后，必须核对所有公开 `IStreamRequest` 的 `AuthorizeRequirement`，测试种子角色必须覆盖对应权限；无权限场景应返回干净 401/403，不能表现为 SSE 已写 200 后断流。
- MediatR 管道顺序固定为 Telemetry -> Validation -> Authorization；新增 Validation behavior 时必须同步至少一个真实 validator 和测试，不能提交空壳管道。
- MediatR telemetry 必须复用现有 OpenTelemetry / structured logging，只记录 request type、kind、耗时、结果和异常类型；不得记录 prompt 全文、SQL、token、连接串、API key 或业务数据明细。
- 事务/审计拥有者必须显式且唯一：普通 repository 保存的业务、Outbox、审计和 commit marker 原子性由唯一 `PersistenceCommitEngine` / `RepositoryPersistenceCommitter` 链拥有；Identity 用户/角色事务由 `ITransactionalExecutionService` / `IdentityTransactionalExecutionService` 复用同一 engine，禁止恢复已删除的 `EfTransactionalExecutionService` 或另起手写 transaction/retry。返回 `Result` 的 Identity 写命令必须走 `ExecuteResultAsync`，非成功结果先回滚全部 Identity 中间保存；确需保留的拒绝审计只能在业务事务回滚后另行原子提交。MediatR behavior 不得开启事务、保存审计、调用 `SaveChangesAsync` 或包裹 stream/handler 形成隐式事务边界。
- 没有真实事件生产者的 DbContext 不得为“未来可能使用”复制 Outbox `DbSet`、映射或 `SaveChangesAsync` 领域事件扫描；AiGateway `Session` 领域事件和 RAG delayed integration-event factory 只能通过 repository commit participant 物化到短生命周期 `OutboxDbContext`，业务 Context 不再映射共享 Outbox；主 `AiCopilotDbContext` 是 Outbox 与 persistence marker 的唯一 migration owner。
- EF execution-strategy 重试只能使用官方 `ExecuteInTransactionAsync(... verifySucceeded ...)` 或等价官方入口，不得手写业务重试循环。每次 repository attempt 对业务 Context 只执行一次 `SaveChangesAsync(false)`，只有事务确认成功后才 `AcceptAllChanges`、清领域事件或清 RAG factory buffer。
- COMMIT 已成功但 ACK 丢失必须由同事务 durable commit marker 和独立 fresh context 验证，不能用 `SaveChanges(false)`、可选 Outbox 或可选 audit 推断成功；marker 写入后不得再由 caller cancellation 中断 commit/verification。fresh verification 无法确认时必须返回 `persistence_commit_outcome_unknown`，禁止自动重放业务或删除可能已提交写入对应的文件，必须先按 commit id 对账再决定重试。
- RAG `UploadDocument` 与 AiGateway SessionTemp/AgentInput `UploadRecord` 数据库绑定上传文件只能通过 `IPersistenceFileStorageService` 保存：先持久化 `.persistence/file-reconciliation` 对账日志，再写物理文件，并让后续 repository 保存复用同一 commit id；没有真实业务行变化时禁止确认文件。请求侧和 DataWorker 对账侧必须用同一 PostgreSQL advisory lease 防止活跃上传被清理；commit 结果未知时保留日志和文件，确认提交后只删日志，确认未提交后才删文件。HttpApi 与 DataWorker 必须共享 `/var/lib/aicopilot` 持久卷，日志损坏时 marker 清理必须 fail-closed，禁止 cron、手工 `rm` 或第二套清理器绕过该边界。
- 数据库绑定上传调用方必须复用唯一 `PersistenceFileCommitProtocol`，禁止各自复制 unknown/rollback/confirm catch。repository 未消费预留 commit id（包括 callback 漏掉 `SaveChanges`）时，确认必须 fail-closed、删除未提交文件并保留失败信号，不得仅凭 callback 正常返回清 journal。
- RAG 文档删除事件不得绕过待对账上传：删除物理文件前必须按 storage path 查询 journal；有记录时取得同一 commit lease、在锁内复查并持久退休 journal 后才能删文件，journal 不可读或 lease 活跃必须让消息重试。journal/file 删除即使目录项当前已不可见，也必须完成父目录 durability barrier 后才能继续；日志和审计不得记录原始客户端路径。
- commit marker 必须按 `created_at_utc` 索引并由 DataWorker 分批清理；默认保留 30 天，保留期必须长于文件对账延迟，存在待处理日志的 marker 不得删除。`AI-PERSIST-01b/01c` 共同定义完整持久化边界，后续修改 Identity、文件上传、marker、共享卷或 DataWorker 清理时必须同时跑真实 PostgreSQL `Suite=PersistenceCommit`、migration 与全量门禁；局部绿测不得替代整体验收。
- `ArtifactWorkspace` 仍是独立的多文件、覆盖和目录复制边界，不得伪装成已被单文件上传 stage 覆盖。`AI-PERSIST-01d / AI-SEC-047` 完成前，禁止宣称所有数据库绑定文件已原子化；治理时必须同时处理新文件、已有文件覆盖、版本归档、final 复制和 commit-unknown 恢复，不得硬套单文件 API。
- 知识库文件唯一写入口是 RAG Document API；AiGateway 只允许 SessionTemp/AgentInput upload。禁止恢复 `UploadRecordScope.KnowledgeBase` 新写入、`IRagDocumentUploadBridge` 或 RAG→AiGateway 同步 shadow record。历史 KB shadow 行/列/枚举字符串由 `AI-PERSIST-01e / AI-SEC-048` 在只读盘点、证据导出和 drain 旧 HttpApi 后物理清理，禁止为冗余影子链新增 saga、幂等键或兼容 adapter。
- RAG 的应用层 hash 预查不是并发幂等保证；`AI-SEC-050` 完成前不得宣称同 KB/同文件并发上传已去重。治理必须先盘点既有重复数据，再以数据库唯一约束、冲突后的既有 Document 语义和真实 PostgreSQL 并发测试闭环，失败竞争者仍须走 file journal 安全回滚。
- “至少一个 enabled Admin”是跨 Identity 命令的不变量。当前 `DisableUser`、`UpdateUserRole` 和 migration seed 必须在唯一 Identity transaction 内先取得固定全局 PostgreSQL transaction advisory lock，再读取用户、角色和 enabled Admin；execution-strategy 每次 retry 必须重新加锁并重读。当前生产树没有 DeleteUser 入口；未来新增删除、直接角色移除或其它减少可用管理员数量的路径，必须同时接入该边界、编译型架构 Analyzer 和真实 PostgreSQL 竞争测试，禁止只加应用层 count 或制造空壳兼容 API。
- enabled Admin 的“数量存在”与“具备恢复治理能力”是两个不变量。Admin 角色的最小恢复权限基线由 `AI-SEC-052` 单独治理；不得用 AI-SEC-051 的人数绿测证明 Admin 权限集合仍可恢复。
- HTTP `ProblemDetails.extensions` 中的 `code` 与 `traceId` 是大小写不敏感的保留键：descriptor extensions 中所有大小写变体必须在复制时丢弃，再由 `ApiProblemDescriptor.Code` 和当前 `HttpContext.TraceIdentifier` 以唯一 canonical `code`/`traceId` 写入；descriptor、异常或其它调用方不得覆盖、伪造或注入歧义键。

## Test Architecture Governance

- 三项目测试架构专题入口是 `../docs/三项目测试架构治理总计划.md`。AICopilot 测试资产身份只来自项目元数据，测试清单只负责真实发现 runner，CI 只依赖实际执行结果对账。
- 新增测试必须进入 `Unit/Aggregate/Application/Workflow/Contract/Conformance/Persistence/HttpIntegration/EndToEnd/Deployment/GoldenEval/Architecture` 等物理项目，直接声明 `IsTestProject` 与 kind/runtime/cadence/owner/required 元数据并进入 `AICopilot.slnx`。不得恢复混合测试桶，不得使用 `Phase/Batch/Suite` filter 充当物理分层。
- support code 只能位于 `src/testing` 的五个固定 TestKit 项目，必须直接声明 `IsTestProject=false`、owner 和完整 consumers，不得引用 test SDK、xUnit/NUnit/MSTest、FluentAssertions/Shouldly 等测试框架或断言 package，也不得声明 Fact/Theory。xUnit 生命周期适配和断言 helper 必须位于实际 runner，不得为 support 恢复 package 例外 allowlist。其它路径不能靠自声明 Support 逃避 discovery；新增 support 项目必须先修改固定项目清单和行为负例。
- `AICopilotTestKind=Aggregate` 的 runner 必须是 `Runtime=Pure`，且只能直接引用 core/shared 领域项目；业务语义 catalog、application policy 和 service 编排必须进入 `ApplicationTests`。`AICopilotTestKind=Application` 也必须是 `Runtime=Pure`，不得直接引用 host、EF/Dapper、Aspire 或 Persistence TestKit；文件系统持久化语义只能进入 `PersistenceFilesystemTests`，不得恢复 `ApplicationFilesystemTests`。
- Runner/TestKit 的依赖身份必须来自指定 Configuration 下 MSBuild evaluated `ProjectReference` / `PackageReference` 图；必须覆盖隐式 `Directory.Build.*`、递归 import、生效条件、逐 TargetFramework item 和 TestKit 传递闭包。Raw csproj/字符串扫描不得作为依赖边界证据；评估失败、缺失绝对 `FullPath` 或 package identity 必须 fail-closed。Aggregate/Application direct boundary、Pure transitive boundary、TestKit consumer 双向对账和 production→TestKit 禁令都必须使用同一 evaluated 图。
- Required lane 必须动态发现所有 required .NET runner，逐项产生 TRX，并要求每个 runner `discovered>0`，再对账 inventory `caseCount=discovered=executed`、`failed=0`、`skipped=0`；不得在 workflow 另写一份 .NET case 总数。缺 Docker/Aspire/Browser 要在 preflight 失败，禁止 Skip、`continue-on-error` 或仅跑 filtered subset 换绿。
- Canonical 完成证据必须同时对账 inventory 中全部 required .NET runner、Vitest、Playwright 和 deployment behavior。当前 Web/Shell 基线精确冻结为 Vitest 165、Playwright 43、deployment behavior 33，任一实际数量与基线不等即失败；有意增删 case 必须同批更新测试、对账基线、CI 和文档，不得用 `>0` 换绿。定向 `--filter` 只能用于失败诊断；活动规则、契约或清单中的定向命令在测试迁移、重命名或换物理 runner 时，必须用同一项目和 filter 的 `--list-tests` 证明至少命中 1 项，0-hit 即使命令退出 0 也不是有效诊断证据。
- Required coverage 只有绑定 clean committed HEAD 的 schema v3 inventory 才是权威证据：inventory 必须记录完整生产源码、程序集和 portable PDB 哈希。每个 required runner 必须恰好一份 TRX、至少一份由该 TRX 唯一绑定的 XPlat coverage 物理副本；VSTest deployment/collector 产生的多份副本只有 SHA256 完全相同才可归一为一份逻辑报告，内容冲突或同一 digest 跨 runner 复用必须 fail-closed。17 个 required runner 当前必须形成 17 份逻辑报告，合并后观察全部生产程序集和全部含 sequence point 的生产源码；Cobertura `sources/source + class filename` 只能解析为 RepositoryRoot 内唯一精确生产路径，外部伪生产路径或多根歧义必须失败。工作树不干净、HEAD 不一致、程序集/PDB 哈希不一致、逻辑报告缺失/新增或新增生产源码未被任何 required runner 加载都必须失败。在 inventory 之后执行的真实临时 csproj Analyzer 夹具只能通过 `<Analyzer Include>` 消费已绑定的 Release Analyzer DLL，禁止用 `ProjectReference` 重建或改写生产 Analyzer 输出，且夹具必须断言 DLL/PDB 指纹执行前后不变。Aspire 子进程宿主不在 testhost 内插桩时，必须抽取由真实 `Program` 和测试共用的生产组合入口并验证实际 DI/行为，禁止用空反射、无语义 type touch 或专用 coverage wrapper 换绿。coverage baseline 只能在完整 universe 已实际观察且指标不回退后更新；required runner 数有意变化时，只允许在完整 clean-HEAD 实测的 `-UpdateBaseline` 运行中同步校准逻辑报告数，no-update 仍必须 fail-closed。
- Canonical required build 必须把 `git rev-parse HEAD` 得到的完整 commit 作为唯一 `SourceRevisionId` 传给整份 solution build，避免 synthetic merge/head 或多次 project build 生成不同 `AssemblyInformationalVersion` / PDB identity。inventory 必须按每个 required runner 的 Release evaluated runtime `ProjectReference` 传递闭包精确冻结应存在的生产 DLL/PDB 集合，缺失、半对或 closure 外生产 pair 都必须失败。每个并行/串行 runner 紧邻 `dotnet test` 前必须独立执行 launch binding：只允许通过显式 `-SynchronizeRunnerBuildIdentity` 从 inventory-bound canonical production 路径逐对覆盖该精确 closure 内的不一致 pair，禁止把全部生产程序集盲拷进全部 runner；binding 必须原子持久化 HEAD、inventory digest、target、同步前/后哈希、同步标记和 canonical digest，失败不得覆盖既有证据。coverage 必须完整消费 1 runner/1 launch manifest，校验其与 inventory closure、TRX case-preserving `TestMethod.codeBase`、VSTest lowercase storage identity、canonical production 输出和测试后 runner 全部 manifest DLL/PDB 精确一致；只检查 coverage observed assembly 或测试后临时状态不能冒充启动输入。inventory 生成后不得再单独重建生产项目；确需重建时必须重新统一 build、重生 inventory，并为全部 runner 重新绑定。
- Required 质量门禁只能使用事件固定的一个比较基点：PR 使用 base SHA，main push 使用 before SHA；不得提供 `workflow_dispatch`、手工 snapshot、输入参数、环境变量或第二脚本入口选择质量基点。比较 commit 为空、全零、无法解析、等于 candidate HEAD 或非祖先都必须失败。base 已有同类 baseline 时执行 no-regression ratchet；base 尚无 baseline 时只允许首次 bootstrap，candidate baseline 必须与当前 inventory/coverage/duplication/mutation 实测精确对账。Bootstrap 的“base 不存在 baseline”必须通过成功的 Git tree identity 查询判定，禁止依赖预期失败的 `git show` 后继续执行并泄漏非零 `$LASTEXITCODE`，造成质量已通过但 CI shell 假失败。该比较基点只用于质量回归对账，不是代码授权、review、waiver 或 trust root。
- 旧测试声明迁移账本只能锚定不可变 Git commit 中的真实 `src/tests` tree、声明数和有序符号摘要；不得引用已删除的 governance baseline、generator 或其它影子入口。
- compatibility baseline 只能记录真实兼容/迁移项，且已经 bootstrap 后只能删除或收紧 deadline/call-site 上限，不得新增 ID、扩大调用方或换名回避。普通 abstraction 不进 baseline；它可正常新增或演进调用方，但 inventory 必须证明唯一活跃声明、至少一个真实可执行调用点、`AI-ORDINARY-*` 身份且不含 deadline/callEvidence/coverageTests 等兼容生命周期字段；注释、字符串和声明不算调用方。
- 同一 compatibility 项跨多个公开 legacy surface 时，caller symbol roster 必须列出完整精确符号并集；若多个 surface 可在同一生产调用成员内共同出现，调用方上限必须使用 `distinct-caller-member` 按非空 Roslyn `EnclosingMemberSymbolId` 去重，无法解析 enclosing member、未知 count mode 或遗漏任一 surface 都必须 fail-closed。producer 路径必须显式排除，任一 surface 新增生产调用成员都必须命中同一既有上限，不得把 surface 数量相加后放宽 baseline。
- MCP TestKit 的真实 executable 与供 Conformance/EndToEnd 使用的 in-process server 如暴露同一 canonical tool，名称和只读/破坏性 annotation 必须一致；变更后必须先用精确 FQN 的 `--list-tests` 证明目标 E2E 恰好发现 1 项，再真实执行该 E2E，不能只以 support 源码编译或其它 Conformance case 冒充闭环。
- `currentSessionId` 可能只来自 `sessionStorage`，只能表示待解析的持久化候选，不能作为 Chat、Plan、Upload 或其它服务端动作的授权目标；动作必须使用已存在于当前 session list 且已完成历史/审批/任务激活的 resolved session，并在 UI 和 store action 两层复核。进入或重新进入 ChatView 时必须在子组件渲染前同步撤销旧动作权限。初始 `null -> A` 水合与 `A -> A` 刷新不得清空用户已输入的草稿、模式或高级选择；刷新失败必须恢复原 active/raw 会话并保留 composer，只有已解析会话 `A -> B` / `A -> null` 才执行 reset。会话激活、流、任务、上传、预览、下载、在岗声明、轮询、历史分页或删除在途时，选择/新建/删除及 SPA 离开 Chat 必须由同一 store+router 临界区 fail-closed；DELETE ACK-unknown 只能在 session list 确认目标仍存在后恢复，禁止旧 A 响应污染 B。
- 会话、任务、工作区、产物和审批均是带归属的权威投影：只允许已加载 roster/current task 中的 ID，下载必须从当前工作区解析 canonical metadata；task/workspace/approval 任一步读取失败必须原子恢复上一代可信投影。审批投影未知时允许编辑草稿但禁止 Chat/Plan/审批/任务/最终输出 mutation；只有同一会话/任务的权威刷新成功才能解除门禁。DELETE 超时或断链属于 ACK-unknown，必须先用 session list 对账，无法对账时保持 resolved authority 为空，禁止假定删除未发生并恢复旧会话。
- Required workflow 中任何经 `tee` 保存证据的 Shell 测试必须显式使用 `set -euo pipefail` 并把 stderr 合并进证据流；不得让 `tee` 的成功码覆盖左侧测试失败。最终 reconcile 是第二道对账，不能替代测试 step 自身的正确失败传播。
- Required CI 可以把治理行为/ratchet、canonical .NET、Web/deployment 和 mutation 拆成同一 run 内并行 lane，但每个 lane 必须独立恢复其真实工具链输入，不能依赖兄弟 runner 的工作目录；governance behavior 在调用 TypeScript compatibility probe 前必须安装 lockfile 固定的 Node/TypeScript 依赖。唯一 `build-test` 汇总门禁必须用 `always()` 检查每个 lane 的真实 conclusion，任一非 `success` 都 fail-closed；只有全部 lane 成功后才允许下载本 run 的固定名称 artifact、合并证据并执行最终 case 对账。`ForEach-Object -Parallel` 中的 per-runner launch binding 必须各自启动隔离的 `pwsh -NoProfile -File` 进程并在退出码成功后紧邻执行 `dotnet test`，不得在共享 runspace host 内直接调用 binder；禁止跨 run 复用 artifact、让某个 lane 退出汇总依赖、把中间 artifact 当最终完成证据，或用并行化删除测试、Skip、过滤、放宽 coverage/mutation/数量断言换取时长。
- `aicopilot-simulation-release-candidate.yml` 只能作为 Manual-only 完整非生产 Simulation acceptance：使用固定 Linux runner，先以 `docker info` fail-fast，再整项目执行 `AICopilot.SimulationTests` pure runner 与 `AICopilot.SimulationDockerTests` 真实 Docker runner，二者均不得 Skip；不得在 PR 重复 Backend/Web/required suite，不得用 `--filter`/Phase/Batch/Suite/类名或静态 changed-files 清单选测。两个 runner 必须分别产生 TRX 并对账 12/1，报告、JSON 摘要和 TRX 只能写入已 ignore 的 `artifacts/simulation/`，workflow 必须以 `always()` 上传 evidence，不得生成独立文档流水。
- `CloudAiReadLiveTests` 只允许显式 Manual/Release 的非生产真实契约执行，缺环境必须失败；不得纳入普通 PR，也不得以 Stub/Simulation 代替真实 Cloud provider。
- `GoldenEvalTests` 必须穿过真实 `AgentWorkflowPipeline` 或其生产正式执行组件，使用版本化期望输出并在数据集内记录变更理由；直接调用 leaf policy/formatter、自证输入、仅匹配 fixture 文本或不经生产编排路径的 case 不得作为 Golden hard gate。
- `AICopilot.Architecture.Analyzers` 是生产项目的编译型架构 owner：根 `Directory.Build.targets` 必须把它以 Analyzer 引用接入每个非测试生产项目，并保持 `Microsoft.CodeAnalysis.CSharp 5.6.0` 精确版本。`AIARCH001`–`AIARCH007` 全部必须是 `Error + IsEnabledByDefault + NotConfigurable`，CompilationEnd 规则还必须保留 `CompilationEnd` tag；禁止用 `NoWarn`、`.editorconfig/.globalconfig` severity、`#pragma warning disable`、`SuppressMessage/UnconditionalSuppressMessage`、降级 warning、可选开关或第二套同义门禁规避。inventory 必须 fail-closed 扫描生产源码与 Analyzer 配置，真实临时 csproj build 必须证明上述抑制无法换绿。
- `AIARCH001` 必须以当前真实生产项目名显式分类；任何未分类的 `AICopilot.*` 生产源项目或目标项目必须 fail-closed，不得以 Unknown 继续编译。
- `AICopilot.Architecture.AnalyzerTests` 必须同时保有语义正/反例和每条 Rule ID 的真实临时 csproj 编译失败夹具；属性、Controller/action、descriptor 和例外只能按完全限定 symbol identity 识别，同名类型/伪属性不得被当作正式元数据。`AIARCH004` 还必须证明减员实际位于唯一 transaction delegate 内且 invariant guard 在同一执行路径上先于变更；inline/stored lambda、inline/stored method-group 和局部 delegate 别名都必须建立 edge-aware caller→delegate 边，synthetic transaction edge 与真实 invocation edge 必须分开，同一 target 经事务调用后又被直接调用仍必须失败。`AIARCH006` 必须对每个源码方法（含 internal/private/protected HostedService 路径）先判断该方法自身是否直接调用、构造、泛型解析完全限定真实 Cloud operation，签名/字段/ctor 是否持有正式 client，或自身是否为正式 Cloud workflow；只有自身命中后才遍历其完整 reachable graph 查副作用。不得因 generic orchestrator 的深层 interface dispatch 某一实现可能读 Cloud 就把整个编排器误标为 Cloud root；同名 Cloud 类型、仅计划/DTO 类型或方法名也不得扩大可信入口。`ArchitectureTests` 只保留需要运行时、反射、数据库或动态组装才能证明的事实。
- 跨项目 Analyzer 调用图必须由源生成器输出版本化、定长上限、精确 `producer assembly + contract assembly + documentation method id` 摘要，消费方必须校验数量、producer 身份和全量内容一致。object creation 的 delegate 实参必须按构造参数 symbol 绑定，显式 invocation 的 delegate 实参按调用参数绑定；隐式 optional delegate 默认值不能伪造未知 callback，真实无法解析的 delegate 仍必须 fail-closed。正式 `IAuditLogWriter` 只能以完全限定 contract identity 在产生跨项目边时截断，同名接口/实现不得获得该例外。
- 重复度 baseline 以 `path+line` 作为实例身份，同一文件内多次出现的同 signature 必须逐实例计数；同时锁定分类汇总和每个 signature 的 instance count / duplicated lines / duplicated tokens。base 尚无该 baseline 时允许一次 candidate-exact bootstrap；base 已有 baseline 后，新签名、任一签名指标增长或汇总上限放宽都必须失败，禁止仅重新生成 baseline 换绿。

## Unified Agent Workflow

- `AgentWorkflowPipeline` 是 AICopilot 用户输入的统一工作流主干；当前旧名 `ChatWorkflowOrchestrator` 只可作为待消歧历史名，不代表“仅聊天可用”。
- Chat 模式和 Plan 模式都必须复用统一管线的意图理解、上下文编排和能力发现；两者只能在出口行为不同。
- Chat 出口可以按现有安全策略直接生成回答，或执行已允许的低风险只读动作。
- Plan 出口只能生成 `PlanDraft` 计划草案；用户确认前不得执行 Cloud 查询、MCP 工具、Tool 调用、Worker 入队或其他真实业务动作。
- `PlanAgentTaskCommand` 只能负责计划草案/任务状态的持久化和编排入口，不得独立实现意图理解、工具发现、Skill 选择或 Tool catalog 强校验。
- Skill、Tool、MCP 或 DataSource 未匹配时，不得阻断 `PlanDraft` 生成；只能在草案里说明能力缺口或要求用户补充目标。
- 用户确认 `PlanDraft` 后，才允许转换为 `ExecutablePlan` / `AgentTask`，并进入 Skill、Tool、Schema、Guard、审批和 Worker 执行链路。
- Cloud 只读 Agent 当前正式能力覆盖 `Analysis.Device.List/Detail/Status`、`Analysis.DeviceLog.Latest/Range/ByLevel`、`Analysis.Capacity.Range/ByDevice`、`Analysis.ProductionData.Latest/Range/ByDevice`、`Analysis.Process.List/Detail` 和 `Analysis.ClientRelease.List`；`Analysis.Capacity.ByProcess` 尚未形成正式聚合契约，不得宣称可用。全部已覆盖能力必须复用统一语义定义和唯一 Cloud AiRead 实现，不能另起隐藏查询链、伪造返回或降级到其他数据源。
- Agent workflow 的阶段和并行分支必须由 `AgentWorkflowTopology` 显式声明；`Tools`、`Knowledge`、`DataAnalysis`、`BusinessPolicy` 四个分支必须保持 `Task.WhenAll` + `AgentWorkflowSink` fan-out/fan-in 模式，不得为了“管道化”拍平成串行或为新能力另起一条孤立链路。

## Capability Boundaries

以下能力必须分开：

- Intent routing。
- RAG 知识检索。
- DataAnalysis / Text-to-SQL。
- MCP 工具执行。
- Human-in-the-loop 审批。

不能因为实现方便把它们合成一个大 agent 或大 service。跨能力改动前必须说明边界影响。

## Safety

- Preview/prerelease NuGet 包默认禁止。
- 已知漏洞依赖默认阻断。
- Known-vulnerable dependencies are forbidden; NU190x and npm audit findings must be handled before acceptance.
- 禁止硬编码 API key、token、secret、license、provider credential、数据库凭据、MCP 凭据。
- HttpApi JWT 配置必须通过唯一运行时校验入口 fail-fast：Issuer、Audience 非空，SecretKey 至少 64 字符，token lifetime 大于 0；部署脚本校验不能替代运行时校验，错误不得回显 secret。
- AICopilot 生产 Docker 构建必须使用 Harbor mirror 的基础镜像，包括 .NET ASP.NET runtime、Node、Nginx、PostgreSQL、RabbitMQ 和 Qdrant；workflow、Dockerfile 和应急构建脚本不得默认从 Docker Hub 或 MCR 拉生产基础镜像。
- AICopilot 生产发布标准路径是本机构建镜像、推送 Harbor、再通过 SSH 触发服务器 `deploy-release.sh`；`aicopilot-image` / `aicopilot-deploy` 只允许作为带确认词的灾备入口。单镜像 build/push 默认 15 分钟超时，Harbor 检查默认 2 分钟超时，SSH deploy 默认 30 分钟超时；超时必须停止并诊断 Docker buildx、Harbor tag、服务器 compose/logs 和 release 状态，不得继续等待 GitHub Actions 或 shell 命令自然超时。
- 工作区根 `deploy/Deploy-Changed.ps1` 是日常应用唯一对外入口；正式发布要求本地 `main` 已提交并自动 push 到 `origin/main`，读取生产 SHA 后按 Git 改动和 `ProjectReference` 依赖闭包只构建、推送 Harbor、部署受影响镜像。影响无法安全归属必须停止，禁止退化成全量。`Deploy.ps1 -Target AICopilot -Services ...` 只允许作为统一入口内部执行器或显式恢复入口；`Auto` 在 SSH TCP 不可达时使用 `aicopilot-routine-request.yml` self-hosted Runner。HttpApi/DataWorker/RagWorker 命中时自动包含 migration。
- 同一候选中顺序构建 HttpApi、DataWorker、RagWorker 和 migration 时，源码 detached worktree 与每个服务的私有 `--artifacts-path` 都必须位于无路径别名的统一部署 artifacts 根；禁止把源码或 SDK artifacts 放进 macOS `TMPDIR=/private/var/folders/...`、复用共享 `bin/obj`、混用 `/private/var` 与 `/var` 路径别名或依赖构建顺序，避免一个入口点的依赖图污染另一个入口点。
- AICopilot 部署策略架构测试 `deploy/enterprise-ai/tests/TestDeploymentPolicy.ps1` 必须接入 `dotnet build` 和 CI；删除生产状态 inspect、迁移闭包、显式服务集合或重新把全量作为日常默认时，构建必须失败。
- 稳定服务器 Runner 常规安装/升级必须独立执行；SSH 不可达且 Runner 完全缺失时，`aicopilot-routine-request.yml` 只允许从已推送提交一次性安装 pinned Runner，已有文件不得覆盖。日常应用发布不得同步 Compose 或其它 support files；migration 前必须备份 PostgreSQL，常驻服务失败必须回滚镜像，cleanup/GC/深度 attestation 不得阻塞健康应用发布。
- AICopilot support sync 必须包含 compose 并使用 staging + SHA256 + reservation token；support install 与 release 必须绑定同一全局锁和 digest，禁止同步 `.env`、release state、锁和备份。持久状态变更前失败必须恢复 `.env` 与 release state；active/stale lock、普通退出码、真实 timeout `124`、信号退出和同 SHA 健康幂等必须通过行为测试。
- 正式 release/no-op 必须同时绑定 workspace plan/profile digest、完整 Git SHA、显式服务闭包、support/services/image manifest、应用 immutable OCI digest、服务器 `.env` canonical fingerprint 和真实运行容器身份。全局配置 fingerprint 漂移时禁止部分服务发布；HttpApi/DataWorker/RagWorker 必须显式闭包 migration。常驻容器必须 Running、非 Restarting、非 OOM、RestartCount 稳定，已有 Docker Health 时必须 healthy，才允许提交或 no-op。
- 内网 HTTP Harbor 推送后的 digest 解析不得依赖 `buildx imagetools inspect` 的 HTTPS 假设；HTTPS inspection 失败时必须用 `docker manifest inspect --insecure --verbose`，并且只接受唯一 `linux/amd64` descriptor digest。禁止退化为可变 tag、任取 attestation manifest 或跳过 immutable digest 绑定。
- support、compose、release state、PostgreSQL/RabbitMQ/Qdrant 和全部常驻 runtime 必须作为同一恢复事务验收；基础设施回滚必须使用事务前冻结的真实 RepoDigest/runtime image id，不能重新解析可变 tag。恢复或阻断证据落盘不确定时必须返回 `86` 并保留永久 blocked/backup 证据；reservation adopt/cancel 必须是原子 transition，SSH 断联后必须按 invocation token 对账，active/unknown 返回 `87` 且禁止取消、重试或猜测成功。
- `deploy-release.sh` 的失败恢复在 `EXIT` trap 动态上下文中执行；恢复链禁止 bare `return`，提前成功返回必须显式 `return 0`，正常执行到函数末尾仍可使用最后命令状态。恢复函数必须在可捕获的子 Shell 中运行，使内部遗留的 `exit` 也能统一收口为 `86`、blocked evidence 和 terminal invocation state。测试必须在 native Linux Bash 上同时证明恢复成功的全 runtime/probe/attestation 与恢复内部失败的安全收口。
- AICopilot 当前内网生产部署红线是 HTTP-only：不得把 HTTPS redirection、HSTS、nginx 443 listener、证书申请/续期或 OIDC HTTPS metadata 强制校验列为当前修复门槛；后续如需切换 HTTPS，必须由用户单独批准传输层方案和证书来源。HTTP 部署下只能使用兼容 HTTP 的安全加固：内网隔离、端口收敛、同源代理、CORS 白名单、短期 token、不在 URL/日志/审计中暴露 secret、非 root 容器、强 secret、API 只读边界和除 HSTS 外的安全响应头。Cloud OIDC 使用 HTTP issuer 时必须显式启用内网 HTTP OIDC，只允许 loopback、私网 IPv4 或保留内网 DNS 后缀（`.internal.example`、`.internal`、`.lan`、`.local`），公共 HTTP 域名仍必须拒绝。
- AICopilot Web 到 HttpApi 的标准生产路径必须走 nginx 同源 `/api/` 反代；HttpApi CORS 默认不开放跨源。确需浏览器直连后端时，只能配置 `Cors:AllowedOrigins` 中的精确 http/https origin，禁止 `*`、通配子域、带 path/query/fragment 的 origin 或运行时任意放行。
- AICopilot 部署模板、文档示例、滚动复盘、历史诊断记录、workflow 默认值、脚本默认值、migration seed 和 fresh DB seed 不得写入真实内网 IP、弱 secret、`CHANGE_ME`、`dummy-key` 或默认 `root@` 发布目标；真实地址、token 和密码只能来自服务器 `.env`、GitHub secrets、管理员录入或命令行环境变量。root SSH 只允许显式设置 `ALLOW_ROOT_SSH_DEPLOY=true` 的应急路径，不得作为标准发布口径。
- 如果当前真实部署根目录、稳定 Runner、Docker Root Dir、基础设施维护目标、工作区入口参数或标准部署用户与模板不同，必须先更新工作区 `deploy/Deploy.ps1`、`deploy/Invoke-WorkspaceDeploy.ps1`、`deploy/profiles/*.json`、`AICopilot 项目部署与维护指南.md`、`deploy/enterprise-ai/README.md` 和工作区部署总览，再允许继续改脚本或发布。
- 如果当前 `AICopilot` 与 `Cloud` 共用同一台生产宿主机，必须在工作区总入口明确写出共享宿主机事实、共享标准发布人和两个独立部署根；不得把同机双部署根问题写成两套互不相关的环境。
- root 应急路径一旦写入 `releases/*`、`current-release.summary.md` 或 deploy support files，关闭任务前必须恢复 owner/mode，并重新验证标准 non-root `./deploy-release.sh --validate-only`；不得留下 root-owned 状态文件后直接收口。
- 模型 smoke 的 `AICOPILOT_MODEL_SMOKE_API_KEY=dummy-key` 只允许作为真实模型网关的显式兼容例外，必须同时设置 `AICOPILOT_MODEL_SMOKE_ALLOW_DUMMY_KEY=true` 或手工 smoke 命令传 `--allow-dummy-key`；默认 preflight 必须拒绝该弱值。
- AICopilot 模型、Embedding 和 endpoint pool 覆盖 API key 的受保护格式必须使用 `encv2:` AES-GCM；旧 `encv1:` 只能通过 migration worker 或一次性迁移命令重加密，不得在运行时 provider 中继续作为正常解密格式兼容。`AiRuntime:ProviderReliability` endpoint `ApiKey` 和 `ApiKeyEnvironmentVariable` 指向的环境变量值也必须存 `encv2:` 密文，不能放明文。
- 私有模型生产播种只能从服务器真实 `.env` 或本机私密部署手册读取真实值；仓库默认只能使用 `model.internal.example` 占位 URL、空 API key 和禁用状态。新库 seed 的私有模型 context window 固定按 64k（`65536`）口径配置，真实 base URL、API key 和是否启用由 `AICOPILOT_PRIVATE_MODEL_*` 环境变量控制，API key 入库前必须加密为 `encv2:`。
- 模型、prompt、plugin、MCP server、approval threshold 等运行行为优先用配置或明确存储数据，不藏在代码里。
- 容器部署必须显式配置并挂载可写的 `FileStorage:RootPath` 和 `ArtifactWorkspace:RootPath`；标准 compose 将两者固定为共享卷下的 `/var/lib/aicopilot/storage` 与 `/var/lib/aicopilot/artifact-workspaces`，不得通过 `.env` 覆盖到共享卷外，也不得依赖容器内 `LocalApplicationData`、容器层或 `/app` 写入运行产物。
- `/var/lib/aicopilot` 只允许受信任的 AICopilot 后端容器写入；上传路径会拒绝既有 symlink/reparse traversal，但逐段检查不是抵御同 UID 恶意进程竞态的 `openat/O_NOFOLLOW` 沙箱。若威胁模型包含共享卷内不受信任写入者，必须先做容器权限隔离或 dirfd 级原子路径操作，禁止宣称当前静态检查已消除 TOCTOU。
- 当前本地 durable file/journal backend 只支持 Linux 与 macOS；标准生产部署固定使用 Linux 容器。Windows 不得以 `MoveFileEx` 或空操作冒充父目录 durability barrier，必须明确拒绝启动该 backend，或另行实现并验收受治理的 Windows/对象存储 backend。

## Execution

改动前确认：

- 触碰能力：routing、RAG、SQL analysis、MCP、approval、host wiring、frontend、persistence。
- 是否只在 `AICopilot` 内。
- 是否会暗示 Cloud 或 Edge 也要改。
- 是否涉及 Cloud 业务写入边界。

业务不清楚时先问，不猜。

## Code Quality

- 简单数据转换、过滤、投影、分组默认优先使用 LINQ 表达业务意图，尤其 `IQueryable` 必须把过滤、投影、排序和分页下推到数据库。
- 性能热路径、状态机、解析器、流式枚举、数组/`Span<T>` 紧循环允许使用 `for`/`foreach`；不能为了“看起来函数式”牺牲清晰复杂度或引入额外分配。
- 禁止先 `ToList()` / `ToArray()` 再 `Where()` / `Select()` 做本可下推或延迟执行的过滤投影；确需多次遍历时必须显式物化一次并说明原因。
- 真正要拦的是重复枚举、N+1 查询、O(n²) 嵌套、重复扫描和错误数据结构；CA1851 先保持 warning，修完基线后再考虑升为 error。
