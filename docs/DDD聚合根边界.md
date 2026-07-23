# AICopilot DDD 聚合根边界

本文档是 AICopilot 聚合根、投影、队列、审计和运行时记录的长期技术契约。修改领域模型、仓储注册、EF `DbSet`、Agent runtime、timeline 或 approval/workspace 编排前必须先核对本文档和架构测试。

## 总原则

- `IAggregateRoot<>` 只用于能独立维护业务不变量和生命周期的领域根。
- EF `DbSet<T>` 只表示持久化表，不等于 `T` 是聚合根。
- `IRepository<T>` / `IReadRepository<T>` 只能服务真实聚合根。队列、投影、审计、worker 状态和执行过程记录必须使用明确命名的 store/projection/audit 接口。
- AgentTask 执行链路的跨对象一致性必须逐步收口到显式应用层协调器，不能继续扩散为 handler 自行拼多个泛型 repository 和隐式 `SaveChangesAsync`。
- 新增聚合根必须同步更新白名单、说明业务不变量和所属 bounded context；不得用“需要一张表”作为聚合根理由。

## 聚合根白名单

当前允许作为聚合根的类型：

- AiGateway：`Session`、`AgentTask`、`ArtifactWorkspace`、`ApprovalRequest`、`LanguageModel`、`ConversationTemplate`、`ApprovalPolicy`、`RoutingModelConfiguration`、`ToolRegistration`、`ChatRuntimeSettings`、`UploadRecord`。
- DataAnalysis：`BusinessDatabase`、`DataSourcePermissionGrant`。
- McpServer：`McpServerInfo`。
- Rag：`KnowledgeBase`、`EmbeddingModel`、`KnowledgeCategory`、`KnowledgeSupplement`。

`DataSourcePermissionGrant` 当前保留在白名单内，但标记为待评估对象。后续如果权限授权生命周期能完全归入 `BusinessDatabase`，应下沉为 `BusinessDatabase` 子实体或专用权限记录，并从白名单移除。

## 编译型边界

- `AIARCH001` 使用当前真实生产项目名的显式分类图验证引用方向；任何未分类的 `AICopilot.*` 生产源项目或目标项目都必须以 compiler error fail-closed，不得把 Unknown 当作隐式例外。新增生产项目时必须在同一改动中更新分类和语义/真实 csproj 正反例。
- `AIARCH002` 使用 Roslyn symbol 语义检查 `IAggregateRoot` 和完全限定 `AICopilot.SharedKernel.Repository.IRepository<T>` / `IReadRepository<T>`。Repository identity 必须匹配 original definition 或其真实 interface implementation；`Fixture.IRepository` 等同名类型不属于 repository 边界，也不能制造误报。聚合白名单只接受上述完全限定类型名；别名、全局 using、泛型 helper 或换同名 namespace 都不能绕过。修改白名单必须先修改本契约，再同步 Analyzer 与同名 fake 正例、真实违规反例 fixture。
- `AIARCH003` 使用 Roslyn operation 语义阻断未授权项目的 `DbContext`、EF write API、Dapper/Npgsql 与直接 SQL 调用。批准 owner 只有项目 `AICopilot.EntityFrameworkCore`、`AICopilot.Dapper`、仅负责 migration/seed 的 `AICopilot.MigrationWorkApp`，以及精确类型 `AICopilot.Infrastructure.AiGateway.PostgreSqlSessionExecutionLock` 及其嵌套实现。同名类型、adapter、wrapper 或其它 Infrastructure 类型不在例外内。
- `AIARCH004` 对所有减少 enabled Admin 的 mutation 做跨方法语义追踪。Inline/stored 和 field/property 中的 lambda/method-group 都必须解析为 edge-aware caller→delegate 边；field/property initializer、constructor assignment 与 property getter return 在 CompilationEnd 统一解析，不能依赖并发 callback 顺序。Synthetic transaction-delegate edge 与真实 invocation/delegate `Invoke` edge必须分开，同一 target 经事务执行后又被同一或其他 handler 直接调用不能获得 method-global 豁免。分析根包含外部可达方法以及源码图中没有 incoming edge 的 ordinary method，因此 protected `BackgroundService.ExecuteAsync`、internal seeder 和 internal type 的 public entry 不能靠可见性逃逸；只有作为 transaction target 的 private helper 才由 synthetic incoming 归属到真实 caller。Mutation 必须实际位于完全限定 `ITransactionalExecutionService` 的 transaction delegate 内，并由同一执行块/路径上先于 mutation 的 enabled-admin invariant guard 支配；事务、guard、mutation 互不相交、调用顺序反转或 stored/member delegate 直接双用都是 compiler error。同名 transaction/guard 类型和词法影子调用不得成为例外。
- 不允许用 `NoWarn`、降级 diagnostic、添加别名 owner 或保留词法影子门禁来扩大例外。真实 `DbSet<T>` 分类、数据库迁移布局和运行时存储行为仍由下文契约与动态架构/持久化测试验证，不与 Analyzer 重复做字符串扫描。

## 当前架构债

当前聚合根债务清单为空。队列、投影、审计、worker 状态和执行过程记录均不得重新进入 `IAggregateRoot<>` 或泛型 repository 白名单。

`AgentWorkerHeartbeat` 已降级为 worker heartbeat store，不再实现 `IAggregateRoot<>`，也不再通过泛型 repository 注册。
`MessageEvent` 已降级为 session timeline projection store，不再实现 `IAggregateRoot<>`，也不再通过泛型 repository 注册。
`ToolExecutionRecord` 已降级为 tool execution audit store，不再实现 `IAggregateRoot<>`，也不再通过泛型 repository 注册。
`AgentTaskRunQueueItem` 已降级为 agent task run queue store，不再实现 `IAggregateRoot<>`，也不再通过泛型 repository 注册。
`AgentTaskRunAttempt` 已降级为 agent task run attempt store，不再实现 `IAggregateRoot<>`，也不再通过泛型 repository 注册。
`AgentNodeRun`、`AgentEvidenceRecord`、`AgentRunUsageLedgerEntry`、`AgentNodeCheckpoint`、`AgentNodeOutcomeReconciliation`、`ModelQuotaReservation` 和 `ArtifactFileSetOperation` 都是 AgentTask 执行平面的 durable runtime record，不是聚合根；只能通过各自显式 store 在 task/node fencing token 下领取、checkpoint、查询和对账，不得恢复泛型 repository 或让 handler 直接拼接状态迁移。

## Handler 多持久化依赖债务

MediatR handler 直接注入 3 个及以上 repository/store/file store/query service，视为应用层编排扩散债务。`AiGatewayHandlers_ShouldNotAddMultiPersistenceDependencyDebt` 已锁定当前债务清单为空，不允许新增。

已从债务清单移除的 handler 不得回潮，包括 `UploadRecordCommandHandler`、`GetArtifactWorkspaceQueryHandler`、`DownloadArtifactQueryHandler`、`GetArtifactContentQueryHandler`、`GetArtifactVersionsQueryHandler`、`DownloadArtifactVersionQueryHandler`、`GetArtifactVersionDiffQueryHandler`、`UpdateArtifactContentCommandHandler`、`RestoreArtifactVersionCommandHandler`、`GetAgentArtifactPreviewQueryHandler`、`CreateArtifactRevisionCommentCommandHandler`、`RegenerateDraftArtifactCommandHandler`、`SubmitArtifactForFinalApprovalCommandHandler`、`PlanAgentTaskCommandHandler`、`RunAgentTaskCommandHandler`、`RetryAgentTaskCommandHandler`、`CancelAgentTaskCommandHandler`、`GetAgentTaskQueryHandler`、`GetListAgentTasksBySessionQueryHandler`、`GetSessionTimelineQueryHandler`、`GetPendingAgentApprovalsQueryHandler`、`GetAgentTaskApprovalsQueryHandler`、Agent task audit summary/run attempt/run queue 查询 handler。

## DbSet 分类

新增 `DbSet<T>` 时必须归类：

- `Aggregate`：真实聚合根。
- `AggregateChild`：聚合内部子实体，例如 `Message`、`Document`、`DocumentChunk`。
- `Projection`：由权威状态派生的查询投影，例如 session timeline。
- `Queue`：后台执行队列和入队记录。
- `Audit`：审计、执行结果或 outbox 记录。
- `RuntimeRecord`：运行过程记录，不直接承载业务不变量。
- `WorkerState`：worker 心跳或健康状态。
- `IdentityRecord`：身份基础设施记录。

分类必须由架构测试锁住；未分类的 `DbSet<T>` 不能合入。

## Outbox 所有权与重试边界

- `AiCopilotDbContext` 是 `outbox.outbox_messages` 与 `persistence.commit_markers` 的唯一 migration owner，只保留 `DbSet` 和配置，不承载领域事件扫描或运行时 Outbox 发布；`OutboxDbContext` 与 `PersistenceCommitMarkerDbContext` 均通过 `ExcludeFromMigrations` 保持运行时-only。
- DataAnalysis 与 MCP 当前没有领域事件生产者，不映射 Outbox，也不覆盖 `SaveChangesAsync`；不得以“未来可能使用”为由恢复通用扫描或兼容壳。
- AiGateway 当前只有 `Session` 产生领域事件，`AiGatewayDomainEventOutboxSource` 只在 repository attempt 内物化这些事件；RAG 只保留 delayed integration-event factory，由 `RagIntegrationEventBuffer` 在 repository attempt 内物化。两个业务 Context 都不映射共享 Outbox；不得重新混入无生产者的通用 `IHasDomainEvents` 扫描。
- 运行时直接写 Outbox 的零调用 publisher 抽象不保留；持久 Outbox 的发送统一由 `OutboxDispatcher` 领取和发布，调度仍必须保持 `FOR UPDATE SKIP LOCKED` 与 dead-letter 上限。
- 普通 repository 的业务行、Outbox、审计和 commit marker 只能由 `PersistenceCommitEngine` / `RepositoryPersistenceCommitter` 通过 EF 官方 `ExecuteInTransactionAsync(... verifySucceeded ...)` 原子提交；不得恢复已删除的 `AuditTransactionCoordinator`、业务 Context `SaveChangesAsync` override、手写业务重试或双轨事务实现。
- 每个 attempt 对业务 Context 只执行一次 `SaveChangesAsync(false)`。commit 前 transient failure 可以由 execution strategy 重放；只有 durable marker 通过 fresh context 确认后才 `AcceptAllChanges`、清 AiGateway domain events 或清 RAG delayed factories。RAG `DocumentId` 由 PostgreSQL sequence 在进入可重放事务前分配，避免 retry 时依赖未确认的数据库生成主键。
- COMMIT 成功但 ACK 丢失只能用同事务 marker 验证，不能依赖可选 Outbox/audit。marker 写入后 caller cancellation 不再中断 commit/verification；fresh verification 无法确认时抛出带非敏感 commit id 的 `PersistenceCommitOutcomeUnknownException`，禁止自动重放业务。RAG 上传必须保留可能已提交写入对应的文件，等待对账后再决定清理或重试。
- Identity stores 通过 `ITransactionalExecutionService` / `IdentityTransactionalExecutionService` 复用唯一 `PersistenceCommitEngine`，`EfTransactionalExecutionService` 已物理删除。Identity 命令返回非成功 `Result` 时必须回滚 UserManager/RoleManager 的中间保存；拒绝审计只能在业务回滚后另行提交。RAG `UploadDocument` 与 AiGateway SessionTemp/AgentInput `UploadRecord` 必须先落 durable reconciliation journal、再写物理文件，并让 repository marker 复用同一 commit id；PostgreSQL advisory lease 保护活跃请求，DataWorker 在共享卷上按 marker 对账并按保留期清理。RAG 删除事件是同一文件生命周期边界：必须按 storage path 查询 pending journal、取得同 commit lease、锁内复查并先持久退休 journal 后再删文件；不可读或 active 状态只能重试。`ArtifactWorkspace` 的 workspace 初始化、draft 文件集、版本归档、当前文件替换和 final 多文件复制统一由 `ArtifactFileSetOperation` + durable manifest/journal + task/node fencing + 数据库 checkpoint/marker 协调；ACK unknown、hash 漂移、rollback 不确定或 orphan 对账未决时必须保持 reconciliation 状态，不得宣称完成。历史 KnowledgeBase shadow `UploadRecord`、旧列与枚举字符串必须在 `AI-PERSIST-01e` 经数据盘点和维护窗口物理删除，不得恢复 RAG→AiGateway 同步双写或为垃圾影子链建设 saga。禁止恢复双轨事务、无日志上传写入或第二套上传清理链。

## 后续收口顺序

1. 保持白名单和债务清单测试常绿，禁止新增伪聚合根。
2. `AgentTaskLifecycleCoordinator` 已承接用户侧 `Run` / `Retry` / `Cancel` 生命周期编排；handler 不得重新直接拼 `IAgentTaskRunQueue`、`IAgentTaskRunAttemptStore`、pending approval 取消和 run queue audit。
3. `AgentApprovalDecisionCoordinator` 已承接 `Approve` / `Reject` 审批决策编排；handler 不得重新直接拼 `ApprovalRequest`、`AgentTask`、`ArtifactWorkspace`、run queue、权限校验、plan confirmation、timeline 和 audit。
4. `ArtifactWorkspaceLifecycleCoordinator` 已承接 workspace `SubmitFinalReview` / `Finalize` 终审提交与最终产物确认编排；handler 不得重新直接拼 `ArtifactWorkspace`、`AgentTask`、`ApprovalRequest`、run attempt、file store、权限校验、timeline 和 audit。
5. `AgentRuntimeEventRecorder` 已承接 `AgentTaskRuntime` 内的 tool execution audit、Agent tool audit 和 timeline staging；runtime 不得重新直接 new `ToolExecutionRecord`、注入 `IToolExecutionAuditStore`、直接调用 `AgentAuditRecorder.RecordToolAsync` 或持有 `MessageTimelineProjectionWriter`。
6. `AgentTaskRunQueueWorkerCoordinator` 已承接 data worker 队列执行领取、stale lease 恢复、queue item 完成/失败回写和 run attempt 解析；`AgentTaskRunQueueWorker` 只负责轮询、scope、heartbeat 和异常边界，不得重新直接拼 `IAgentTaskRunQueueStore`、`IRepository<AgentTask>`、`IAgentTaskRunAttemptStore` 或 run queue audit。
7. `SessionTimelineQueryCoordinator` 已承接 session timeline 查询中的 Session 权限校验、timeline projection、AgentTask、ApprovalRequest、ArtifactWorkspace 聚合读取和 DTO 组装；`GetSessionTimelineQueryHandler` 只保留 MediatR 入口转发。
8. `AgentTaskToolExecutionQueryCoordinator` 已承接 tool execution audit 查询中的 task 权限校验、`IToolExecutionAuditStore` 读取、状态/工具过滤、分页和脱敏 DTO 映射；`GetAgentTaskToolExecutionsQueryHandler` 只保留 MediatR 入口转发。
9. `AgentTaskAuditQueryCoordinator` 已承接 Agent task audit summary、run attempt 查询和 run queue 查询中的 task 权限校验、workspace/audit/tool record/run attempt/run queue 读取、分页和 DTO 组装；对应 query handler 只保留 MediatR 入口转发。
10. `AgentTaskDtoQueryService` 已承接 AgentTask DTO 组装所需的 workspace、pending approval 和 active queue 查询；`Run` / `Retry` / `Cancel` / `GetAgentTask` / `GetListAgentTasksBySession` handler 不得重新直接注入 workspace/approval/queue store 或调用静态 DTO composer。
11. `PlanAgentTaskCoordinator` 已承接 PlanAgentTask 草案生成、Session/Upload 校验、intent candidate 适配、唯一 PlanCompiler 封印、plan approval 创建、timeline staging 和 plan audit；`PlanAgentTaskCommandHandler` 只保留 MediatR 入口转发，不得复制步骤或节点编译规则。
12. `AgentApprovalQueryCoordinator` 已承接 pending approval 和按 task 查询中的权限校验、task/workspace 读取、approval 聚合读取和 DTO 组装；approval query handler 只保留 MediatR 入口转发。
13. `UploadRecordCoordinator` 只承接 SessionTemp/AgentInput 上传中的用户与目标校验、上传安全策略、durable file stage、审计和保存；知识库文件只能走 RAG Document API。`UploadRecordCommandHandler` 只保留 MediatR 入口转发，禁止恢复 KB scope、RAG bridge 或同步 shadow record。
14. `ArtifactWorkspaceQueryCoordinator` 已承接 workspace DTO 查询和 artifact 下载中的 workspace/task/approval 读取、owner/privileged 权限校验、final output approval 检查、file store 读取和下载审计；对应 query handler 只保留 MediatR 入口转发。
15. `ArtifactVersioningQueryCoordinator` 已承接 artifact 内容读取、版本列表、版本下载和文本 diff 中的 workspace/task/approval 读取、权限校验、text artifact policy、file store 读取和版本下载审计；对应 query handler 只保留 MediatR 入口转发。
16. `ArtifactVersioningCommandCoordinator` 已承接 artifact 内容更新和版本恢复中的 owner/edit 权限、编辑窗口校验、版本归档、file store 写入、聚合版本变更、审计和保存；对应 command handler 只保留 MediatR 入口转发。
17. `ArtifactWorkspaceP9Coordinator` 已承接 P9 预览、revision comment、draft regenerate 和 artifact final approval 提交中的 workspace/task/approval 读取、P9 policy、file store、审计、状态机更新和保存；对应 P9 handler 只保留 MediatR 入口转发。
18. 后续如引入新的队列、投影、审计、worker 状态或执行过程记录，必须使用明确 store/projection/audit 接口，不能重新挂到泛型 repository。
19. 后续新增 handler 不得直接注入 3 个及以上 repository/store/file store/query service；确需跨聚合或跨 store 编排时必须先进入明确命名的应用层 coordinator/query service。
20. 如确需新增聚合根，必须先说明业务不变量、生命周期边界和所属 bounded context，并同步更新架构测试白名单。
