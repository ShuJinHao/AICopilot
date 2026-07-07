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

- AiGateway：`Session`、`AgentTask`、`ArtifactWorkspace`、`ApprovalRequest`、`LanguageModel`、`ConversationTemplate`、`ApprovalPolicy`、`RoutingModelConfiguration`、`ToolRegistration`、`SkillDefinition`、`ChatRuntimeSettings`、`UploadRecord`。
- DataAnalysis：`BusinessDatabase`、`DataSourcePermissionGrant`。
- McpServer：`McpServerInfo`。
- Rag：`KnowledgeBase`、`EmbeddingModel`、`KnowledgeCategory`、`KnowledgeSupplement`。

`DataSourcePermissionGrant` 当前保留在白名单内，但标记为待评估对象。后续如果权限授权生命周期能完全归入 `BusinessDatabase`，应下沉为 `BusinessDatabase` 子实体或专用权限记录，并从白名单移除。

## 当前架构债

当前聚合根债务清单为空。队列、投影、审计、worker 状态和执行过程记录均不得重新进入 `IAggregateRoot<>` 或泛型 repository 白名单。

`AgentWorkerHeartbeat` 已降级为 worker heartbeat store，不再实现 `IAggregateRoot<>`，也不再通过泛型 repository 注册。
`MessageEvent` 已降级为 session timeline projection store，不再实现 `IAggregateRoot<>`，也不再通过泛型 repository 注册。
`ToolExecutionRecord` 已降级为 tool execution audit store，不再实现 `IAggregateRoot<>`，也不再通过泛型 repository 注册。
`AgentTaskRunQueueItem` 已降级为 agent task run queue store，不再实现 `IAggregateRoot<>`，也不再通过泛型 repository 注册。
`AgentTaskRunAttempt` 已降级为 agent task run attempt store，不再实现 `IAggregateRoot<>`，也不再通过泛型 repository 注册。

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
11. `PlanAgentTaskCoordinator` 已承接 PlanAgentTask 草案生成、Session/Upload 校验、Skill 自动选择、plan document 构建、plan approval 创建、timeline staging 和 plan audit；`PlanAgentTaskCommandHandler` 只保留 MediatR 入口转发。
12. `AgentApprovalQueryCoordinator` 已承接 pending approval 和按 task 查询中的权限校验、task/workspace 读取、approval 聚合读取和 DTO 组装；approval query handler 只保留 MediatR 入口转发。
13. `UploadRecordCoordinator` 已承接 UploadRecord 命令中的用户校验、Session/AgentTask/RAG scope 绑定校验、知识库写权限复查、上传安全策略、file storage、RAG bridge、审计和保存；`UploadRecordCommandHandler` 只保留 MediatR 入口转发。
14. `ArtifactWorkspaceQueryCoordinator` 已承接 workspace DTO 查询和 artifact 下载中的 workspace/task/approval 读取、owner/privileged 权限校验、final output approval 检查、file store 读取和下载审计；对应 query handler 只保留 MediatR 入口转发。
15. `ArtifactVersioningQueryCoordinator` 已承接 artifact 内容读取、版本列表、版本下载和文本 diff 中的 workspace/task/approval 读取、权限校验、text artifact policy、file store 读取和版本下载审计；对应 query handler 只保留 MediatR 入口转发。
16. `ArtifactVersioningCommandCoordinator` 已承接 artifact 内容更新和版本恢复中的 owner/edit 权限、编辑窗口校验、版本归档、file store 写入、聚合版本变更、审计和保存；对应 command handler 只保留 MediatR 入口转发。
17. `ArtifactWorkspaceP9Coordinator` 已承接 P9 预览、revision comment、draft regenerate 和 artifact final approval 提交中的 workspace/task/approval 读取、P9 policy、file store、审计、状态机更新和保存；对应 P9 handler 只保留 MediatR 入口转发。
18. 后续如引入新的队列、投影、审计、worker 状态或执行过程记录，必须使用明确 store/projection/audit 接口，不能重新挂到泛型 repository。
19. 后续新增 handler 不得直接注入 3 个及以上 repository/store/file store/query service；确需跨聚合或跨 store 编排时必须先进入明确命名的应用层 coordinator/query service。
20. 如确需新增聚合根，必须先说明业务不变量、生命周期边界和所属 bounded context，并同步更新架构测试白名单。
