# AICopilot DDD 聚合根边界

本文档是 AICopilot 聚合、持久化集合分类、DbContext 与迁移所有权、审计、Outbox、事务提交和 RAG 文件持久化的唯一技术正文。业务规则和 Agent 契约只保留产品或协议摘要并链接本文，不复制类型清单、仓储算法、Context 矩阵、文件 journal 或保留实现。

## 总原则

- `IAggregateRoot<>` 只用于能独立维护业务不变量和生命周期的领域根；`DbSet<T>` 只表示持久化集合，不自动使 `T` 成为聚合根。
- `IRepository<T>` / `IReadRepository<T>` 只服务真实聚合根。投影、审计、Outbox、配额与运行状态必须使用明确命名的 store 或专用服务。
- 新增聚合根必须说明业务不变量、生命周期和 bounded context，并同批更新本契约、Analyzer 白名单与架构测试。
- MediatR handler 不得自行拼接三个及以上 repository/store/query service；跨边界编排必须进入明确命名的应用服务。

## 聚合根清单

- AiGateway：`Session`、`LanguageModel`、`ConversationTemplate`、`ToolRegistration`。
- DataAnalysis：`BusinessDatabase`、`DataSourcePermissionGrant`。
- McpServer：`McpServerInfo`。
- Rag：`KnowledgeBase`、`EmbeddingModel`、`KnowledgeCategory`、`KnowledgeSupplement`。

`DataSourcePermissionGrant` 是 DataAnalysis bounded context 的正式独立聚合根。独立 `DataSourcePermissionGrantId`、`RowVersion`、授权/撤销生命周期、repository、审计写入和 `(DataSourceId, TargetType, TargetValue)` 唯一目标约束共同构成其独立不变量边界。`DataSourceId` 的强类型是 `BusinessDatabaseId`。

`DataSourcePermissionGrant` 与 `BusinessDatabase` 是两个聚合；跨聚合仅由 Grant 的 `DataSourceId` 引用 `BusinessDatabaseId`，`BusinessDatabase` 不持有 Grant 子实体集合，也不得通过 EF navigation 恢复父子归属。该归属是正式长期边界。

## 持久化集合分类

所有当前持久化实体必须属于下列分类；新增、删除或重分类时必须同步本表和 `DddAggregateBoundaryTests`，不得以新增空壳 `DbSet` 绕过聚合决定。

| 分类 | 当前类型 |
|---|---|
| Aggregate | `Session`、`LanguageModel`、`ConversationTemplate`、`ToolRegistration`、`BusinessDatabase`、`DataSourcePermissionGrant`、`McpServerInfo`、`KnowledgeBase`、`EmbeddingModel`、`KnowledgeCategory`、`KnowledgeSupplement` |
| AggregateChild | `Message`、`Document`、`DocumentChunk` |
| OwnedValueObject | `ModelParameters`、`TemplateSpecification` |
| RuntimeRecord | `AgentSessionState`、`ModelQuotaReservation`、`PersistenceCommitMarker` |
| Audit | `AuditLogEntry`、`OutboxMessage` |
| IdentityRecord | `ApplicationUser`、`ExternalIdentityBinding`、`IdentityRoleClaim<>`、`IdentityRole<>`、`IdentityUserClaim<>`、`IdentityUserLogin<>`、`IdentityUserRole<>`、`IdentityUserToken<>` |

`AgentSessionState` 和 `ModelQuotaReservation` 是运行记录，不是聚合根；AgentSession checkpoint 的会话连续性与 Interrupted 语义仍由 [Agent 工作流与异常契约](./Agent工作流与异常契约.md) 定义。

### AiGateway 七个集合

`AiGatewayDbContext` 只包含七个当前集合或运行记录：

| `DbSet` | 分类 | 所有权 |
|---|---|---|
| `LanguageModels` | Aggregate | 模型端点、token 参数与启用状态 |
| `ConversationTemplates` | Aggregate | 主聊天与内部 Text-to-SQL 模板 |
| `Sessions` | Aggregate | 会话 owner、模式和消息生命周期 |
| `Messages` | AggregateChild | 按 `Session` 与 `Sequence` 管理的消息 |
| `ToolRegistrations` | Aggregate | 工具规范身份与治理元数据 |
| `AgentSessionStates` | RuntimeRecord | Harness session 的加密状态、TTL、乐观并发与中断语义 |
| `ModelQuotaReservations` | RuntimeRecord | 按真实模型调用的预约、结算、回收与 fencing |

未在上表的 AiGateway 类型不得通过恢复旧映射、兼容表或新增空壳 `DbSet` 重新进入主链。消息历史直接按 `Message.Sequence` 分页，不维护第二份时间线投影。

## DbContext 与迁移所有权

| Context | 当前集合与职责 | 迁移所有权 |
|---|---|---|
| `AiCopilotDbContext` | `AuditLogEntry`、`OutboxMessage`、`PersistenceCommitMarker` | 主基础设施 migration owner；唯一拥有 Outbox 与 persistence commit marker 迁移 |
| `IdentityStoreDbContext` | Identity 记录、`ExternalIdentityBinding`；审计只作为事务参与者 | 拥有 Identity 迁移；审计映射使用 `ExcludeFromMigrations` |
| `AiGatewayDbContext` | 上述七个 AiGateway 集合 | 拥有当前单一 Harness baseline |
| `RagDbContext` | RAG 聚合、`Document`、`DocumentChunk` | 拥有 RAG 迁移 |
| `DataAnalysisDbContext` | `BusinessDatabase`、`DataSourcePermissionGrant` | 拥有 DataAnalysis 迁移 |
| `McpServerDbContext` | `McpServerInfo` | 拥有 MCP 迁移 |
| `AuditDbContext` | 审计查询和运行时审计写入 | 无独立 migration |
| `OutboxDbContext` | 事务内物化和短生命周期领取 Outbox | 无独立 migration |
| `PersistenceCommitMarkerDbContext` | fresh verification 与 marker 维护 | 无独立 migration，映射使用 `ExcludeFromMigrations` |

- 六个 migration owner 必须使用各自隔离的 `__EFMigrationsHistory_*`；不得让单一 Context 的迁移或回滚污染其它 Context。
- 修改 `AiGatewayDbContext` 只能重新生成当前 Harness baseline；不得修改其它 DbContext 迁移链来伪造一致。
- `PostgresModelQuotaReservationStore` 是模型配额唯一生产 store，只能经 `AiGatewayTransactionRunner` 写入 `AiGatewayDbContext`；模型调用预约、结算、回收的事务语义不得复制到其它 Context。
- 没有真实事件生产者的 DbContext 不得复制 Outbox `DbSet`、映射或 `SaveChangesAsync` 领域事件扫描。DataAnalysis 与 MCP 不写 Outbox；AiGateway 只从 `Session` 领域事件物化 Outbox，RAG 只使用 delayed integration-event factory，业务 Context 不映射共享 Outbox。

## 编译型边界

- `AIARCH001` 使用显式项目分类图验证引用方向；未分类的 `AICopilot.*` 生产项目 fail-closed。
- `AIARCH002` 使用 Roslyn symbol 语义验证完全限定的聚合与 repository 身份；同名 fake、alias 或换 namespace 不得绕过。
- `AIARCH003` 阻断未授权项目使用 `DbContext`、EF write API、Dapper/Npgsql 或直接 SQL。持久化 owner 只有 `AICopilot.EntityFrameworkCore`、`AICopilot.Dapper`、精确的 migration/seed 组合边界和已声明的 session lock。
- `AIARCH004` 要求任何减少 enabled Admin 的变更必须在同一事务委托中由 invariant guard 先行支配。
- `AIARCH005–007` 继续以 Error 级别固定 plugin、Cloud 只读调用图和授权/安全元数据；不得 `NoWarn`、降级 diagnostic 或添加宽泛例外。

## Audit、Outbox 与事务提交

- 审计写入遵守唯一 Audit writer decision tree：有业务保存点的命令把业务变更和审计行放入同一事务；`auditLogWriter.SaveChangesAsync` 只允许用于没有业务保存点且已被白名单记录的路径。
- `OutboxDispatcher` 统一领取和发布，必须保留 PostgreSQL `FOR UPDATE SKIP LOCKED` 或等价互斥策略以及 dead-letter 上限，禁止多 worker 重复发布同一消息。
- 业务行、Outbox、审计和数据库 durable commit marker 只能由唯一 `PersistenceCommitEngine` / `RepositoryPersistenceCommitter` 在同一数据库事务中提交。每个 execution-strategy attempt 对业务 Context 只执行一次 `SaveChangesAsync(false)`；事务确认后才 `AcceptAllChanges`、清领域事件或清 RAG factory buffer。
- Identity 通过 `ITransactionalExecutionService` / `IdentityTransactionalExecutionService` 复用同一 engine；非成功 `Result` 必须回滚 UserManager/RoleManager 已触发的中间保存，拒绝审计只能在回滚后另行提交。禁止恢复 `EfTransactionalExecutionService`、通用 Outbox 扫描或复制第二套 transaction/retry。
- EF execution strategy 必须使用官方 `ExecuteInTransactionAsync(... verifySucceeded ...)` 或等价官方入口，禁止手写业务重试循环。commit-unknown 不得通过 `SaveChanges(false)`、Outbox 或 audit 是否存在来推断成功。
- 数据库 durable commit marker 只用于事务提交结果验证和 commit-ACK 丢失对账，不是 Agent durable 编排、Tool checkpoint、任务恢复点或工具重放依据。marker 必须与业务写入处于同一事务，并由 fresh context 在独立超时和 execution strategy 下验证。
- marker 写入后 caller cancellation 不得中断 commit/verification。无法确认时返回稳定 503 `persistence_commit_outcome_unknown` 和非敏感 commit id；调用方不得自动重放业务。
- `PersistenceMaintenanceWorker` 只通过 `PersistenceFileMaintenanceService` 对账 RAG journal、清理 commit marker，并通过 `IModelQuotaReservationStore.ReclaimExpiredAsync` 回收过期模型配额预约；它不领取或发布 Outbox。Outbox 由独立托管的 `OutboxDispatcher` 负责。commit marker 默认保留 30 天并按 `created_at_utc` 索引；保留期必须长于对账延迟，有待处理或不可读 journal 时不得删除 marker。

## RAG 文件持久化、对账与存储

- 知识库文件唯一写入口是 RAG Document API。RAG `UploadDocument` 必须先写 durable reconciliation journal，再写物理文件，并与 repository marker 共用同一 commit id。
- RAG 数据库绑定上传路径必须复用唯一 `PersistenceFileCommitProtocol`。repository 未消费预留 commit id 时确认必须 fail-closed，回滚未提交文件并保留失败信号；不得因为 callback 正常返回就清除 journal。
- 请求与 DataWorker 通过 PostgreSQL advisory lease 互斥。提交结果未知时保留文件和 journal；后台看到同一 marker 才保留文件并清 journal，看不到 marker 才删除文件。journal 不可读时停止 marker 清理。
- 默认每 300 秒扫描，只对至少 10 分钟前的 journal 对账，单轮最多 100 条；`AICOPILOT_PERSISTENCE_*` 只能调整这些部署参数，不得把对账延迟设为 0，也不得让 marker 保留期短于对账延迟。不可读 journal 必须 fail-closed，不得手工批量删除 `.persistence/file-reconciliation`。
- 标准容器共享卷只允许受信任的 AICopilot 后端写入。当前路径边界拒绝既有 symlink/reparse traversal，但不把同 UID 恶意进程在检查与打开之间替换目录的 TOCTOU 视为已解决；扩大威胁模型前必须增加容器权限隔离或 dirfd/`openat` 原子路径操作。
- 标准生产容器部署必须把 RAG 可写 `FileStorage:RootPath` 固定为共享卷 `/var/lib/aicopilot/storage`，在该部署中不得回退容器层、`/app`、`LocalApplicationData` 或共享卷外路径。本地 dev/test 未显式配置时可使用现有 `LocalApplicationData/AICopilot/storage` fallback，但不得把它当作生产容器持久化。durable local file/journal backend 只支持 Linux/macOS，生产固定 Linux；Windows 必须明确拒绝该 backend。
- HttpApi、DataWorker 与 RagWorker 必须共享 `/var/lib/aicopilot`。RagWorker 的文档删除 consumer 必须先按 storage path 查询 pending journal 并争用同一 commit lease；journal 不可读或 lease active 时让消息重试，禁止从容器或 cron 直接删文件。文件对账、marker 保留与清理只能由当前维护链执行，不得恢复会话文件、Artifact workspace 或第二套文件 checkpoint。

## 变更门禁

1. 新增或重分类聚合根、`DbSet<T>`、Context、migration owner、审计/Outbox 参与者或文件持久化入口时，必须同批更新本文、Analyzer、Architecture 与受影响 Persistence 测试。
2. 聚合根清单、Context 分类和迁移历史必须与实际模型一致；任何未分类持久化实体或未声明 migration owner 都 fail-closed。
3. 其它活动文档只保留业务/协议摘要和本文链接，不得复制本文的类型清单、Context 矩阵、事务算法、journal、lease、路径或保留实现。
4. 任何 filtered .NET 测试必须先用同项目、同配置、同 filter 的 `--list-tests` 证明非零命中。
5. 真实 PostgreSQL 持久化合同必须覆盖 commit-ACK 丢失、verification transient/persistent failure、caller cancellation 和数据库生成 identity 重放；相关变更由 selector 选中真实 PostgreSQL、migration 与部署配置验证，部署消费边界另见 [AICopilot 安全部署契约](./AICopilot安全部署契约.md)。
