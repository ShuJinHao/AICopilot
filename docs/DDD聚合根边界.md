# AICopilot DDD 聚合根边界

本文档是 AICopilot 领域根、运行记录、审计、Outbox 和持久化事务的活动契约。修改领域模型、仓储注册或 EF `DbSet` 前必须同步架构规则与受影响测试。

## 总原则

- `IAggregateRoot<>` 只用于能独立维护业务不变量和生命周期的领域根；`DbSet<T>` 只表示持久化集合，不自动使 `T` 成为聚合根。
- `IRepository<T>` / `IReadRepository<T>` 只服务真实聚合根。投影、审计、Outbox、配额与运行状态必须使用明确命名的 store 或专用服务。
- 新增聚合根必须说明业务不变量、生命周期和 bounded context，并同批更新本契约、Analyzer 白名单与架构测试。
- MediatR handler 不得自行拼接三个及以上 repository/store/query service；跨边界编排必须进入明确命名的应用服务。

## 聚合根白名单

- AiGateway：`Session`、`LanguageModel`、`ConversationTemplate`、`ToolRegistration`。
- DataAnalysis：`BusinessDatabase`、`DataSourcePermissionGrant`。
- McpServer：`McpServerInfo`。
- Rag：`KnowledgeBase`、`EmbeddingModel`、`KnowledgeCategory`、`KnowledgeSupplement`。

`DataSourcePermissionGrant` 暂作为独立聚合根；若其授权生命周期收回 `BusinessDatabase`，必须在同批从白名单移除。

## AiGateway 持久化分类

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

## 编译型边界

- `AIARCH001` 使用显式项目分类图验证引用方向；未分类的 `AICopilot.*` 生产项目 fail-closed。
- `AIARCH002` 使用 Roslyn symbol 语义验证完全限定的聚合与 repository 身份；同名 fake、alias 或换 namespace 不得绕过。
- `AIARCH003` 阻断未授权项目使用 `DbContext`、EF write API、Dapper/Npgsql 或直接 SQL。持久化 owner 只有 `AICopilot.EntityFrameworkCore`、`AICopilot.Dapper`、精确的 migration/seed 组合边界和已声明的 session lock。
- `AIARCH004` 要求任何减少 enabled Admin 的变更必须在同一事务委托中由 invariant guard 先行支配。
- `AIARCH005–007` 继续以 Error 级别固定 plugin、Cloud 只读调用图和授权/安全元数据；不得 `NoWarn`、降级 diagnostic 或添加宽泛例外。

## Outbox 与事务边界

- `AiCopilotDbContext` 是 `outbox.outbox_messages` 与 `persistence.commit_markers` 的唯一 migration owner；运行时专用 context 使用 `ExcludeFromMigrations`。
- AiGateway 只从 `Session` 领域事件物化 Outbox；RAG 使用 delayed integration-event factory。DataAnalysis 和 MCP 没有领域事件生产者，不得恢复通用扫描。
- 业务行、Outbox、审计和 commit marker 只能由 `PersistenceCommitEngine` / `RepositoryPersistenceCommitter` 通过 EF execution strategy 原子提交。每个 attempt 对业务 context 只执行一次 `SaveChangesAsync(false)`。
- COMMIT 成功但 ACK 丢失只能使用同事务 marker 的 fresh context 验证；无法确认时返回 `persistence_commit_outcome_unknown`，调用方不得自动重放业务。
- `OutboxDispatcher` 统一领取和发布，必须保留 `FOR UPDATE SKIP LOCKED` 和 dead-letter 上限。`PersistenceMaintenanceWorker` 只维护 commit marker、RAG 文件对账和 Outbox 等当前责任。
- RAG 文档上传继续先写 durable reconciliation journal、再写物理文件，并与 repository marker 共用 commit id；其它会话文件入口不属于当前 AiGateway 持久化面。

## 变更门禁

1. 新增或重分类 `DbSet<T>` 时必须同批更新白名单、Analyzer、Architecture 与 Persistence 测试。
2. 修改 `AiGatewayDbContext` 只能重新生成当前 Harness baseline；不得修改其它 DbContext 迁移链来伪造一致。
3. 任何 filtered .NET 测试必须先用同项目、同配置、同 filter 的 `--list-tests` 证明非零命中。
