# AICopilot Remaining Review Matrix

Date: 2026-05-08

Baseline: `main` after PR #37 (`a76d94e`) plus PR #38 RAG document file cleanup. This matrix reconciles the remaining follow-up items from `REVIEW_FOLLOWUP_2026-04-29.md`, `REVIEW_ACCEPTANCE_2026-05-07.md`, and the 5.5 Pro full-code review.

Principle: review documents are inputs, not facts. Current source and tests decide whether an item is closed, still real, deferred, or not applicable.

## Status Legend

| Status | Meaning |
| --- | --- |
| Closed | Current source implements the expected behavior and has direct tests or guard tests. |
| Static-covered | Current source is correct and has static guard coverage, but not a full end-to-end behavior test. |
| Remaining | A real follow-up remains and should be planned before implementation. |
| Deferred | Valid but intentionally outside the current product/runtime scope. |
| Not applicable | The finding does not apply to the current implementation. |

## Matrix

| Source | Item | Current status | Evidence | Suggested next step | Next implementation round |
| --- | --- | --- | --- | --- | --- |
| `REVIEW_FOLLOWUP_2026-04-29.md` P0 | `UpdateUserRole` must invalidate old tokens | Closed | `IdentityAccessManagementTests.UpdateUserRole_ShouldRevokeExistingSession`; `SecurityHardeningTests.UpdateUserRole_ShouldRefreshSecurityStamp` | None | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P0 | Local file storage must reject traversal and cross-root paths | Closed | `SecurityHardeningTests.LocalFileStorageService_ShouldConstrainAccessToConfiguredRoot`; `LocalFileStorageService` | None | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P0 | Outbox must avoid duplicate multi-instance claims and must not retry host cancellation | Static-covered | `OutboxDispatcher`; `SecurityHardeningTests.OutboxDispatcher_ShouldUseSkipLockedAndNotRetryCancellation` | Add integration coverage only if the outbox worker changes again | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P0 | RAG stuck states should be recoverable | Closed | `DocumentIndexingService`; `RagIndexingLifecycleTests`; `SecurityHardeningTests` | None | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P0 | RAG vector rebuild should remove stale chunks | Closed | `KnowledgeVectorIndexWriter`; `RagIndexingLifecycleTests`; `SecurityHardeningTests` | None | No |
| 5.5 Pro review P1 | RAG document upload and `DocumentUploadedEvent` outbox enqueue should be atomic | Closed | PR #33; `RagIntegrationEventStager`; `RagUploadOutboxAtomicityTests`; `SecurityHardeningTests` | None | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P0 | Dapper row limits must not load all rows and truncate afterward | Static-covered | `DapperDatabaseConnector`; data-analysis hardening tests | Add provider-level integration coverage if SQL connector behavior changes | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P0 | Dapper/data analysis should remain read-only | Static-covered | `DapperDatabaseConnector`; data-analysis hardening tests | Keep as a guardrail in future query-builder work | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P0 | Login throttling must not use a global partition key | Static-covered | Security hardening tests and authentication rate-limit source checks | Add behavior test if rate-limit implementation changes | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P0 | Chat stream must not expose raw runtime exceptions | Static-covered | `SecurityHardeningTests.ChatStreamRuntime_ShouldNotExposeGenericExceptionMessages` | None | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P0 | Chat branch sink should complete deterministically | Closed | PR #26; `ChatWorkflowOrchestrator`; `ClaudeFollowupClosureTests` | None | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P0 | MCP command arguments should be parsed safely | Static-covered | `McpServerBootstrap.ParseCommandArguments`; `SecurityHardeningTests` | None | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P0 | MCP SSE timeout should be bounded | Static-covered | `McpRuntimeOptions`; `SecurityHardeningTests` | None | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P0/Future | MCP services disabled/deleted after startup should stop being exposed | Closed | PR #27; `McpRuntimeRegistrySynchronizer`; `McpRuntimeRegistrySynchronizerTests`; `AgentPluginLoader` unregister tests | None | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P1 | JWT role claim dual source could drift from database role | Static-covered | `ClaudeFollowupClosureTests`; `IdentityAccessManagementTests`; permission-attribute authorization | Keep `CurrentUser.Role` audit/display-only; continue rejecting role-claim authorization | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P1 | Approval policy and pending approval long-session semantics | Closed | PR #29; `SessionPolicySemanticsTests`; `ApprovalDecisionValidator`; `ApprovalToolResolver` | None under current-config semantics | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P1 | Template/model long-session semantics | Closed | PR #29; `SessionPolicySemanticsTests`; `FinalAgentBuildExecutor`; `FinalAgentContextSerializer` | If product wants template freeze, create a schema plan first | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P1 | Final agent context store multi-instance behavior | Closed for Redis deployment baseline | `FinalAgentContextDeploymentTests`; `AcceptanceClosureVerificationTests.RedisFinalAgentContextStore_ShouldShareContextAcrossStoreInstances` | Broader message/context transactional consistency remains a design item | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P1 | Identity migration ownership | Closed | PR #28; `MigrationOwnershipTests`; split context snapshots | None | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P1 | Multi-DbContext migration history split | Closed | PR #28; `MigrationSafetyTests`; `MigrationHistoryTables.MigratedContexts` | None | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P1 | Cross-DbContext audit atomicity outside Identity | Closed | PR #31; `AuditTransactionCoordinator`; `AuditTransactionBoundaryTests`; `SecurityHardeningTests` | None | No |
| 5.5 Pro review P1 | RAG/MCP configuration and governance writes need same-level audit transaction coverage | Closed | PR #34; `RagRepository`; `McpServerRepository`; `RagMcpAuditCommandTests`; `AuditTransactionBoundaryTests`; `SecurityHardeningTests` | None | No |
| 5.5 Pro review P2 | MCP SSE endpoint needs explicit target validation | Closed | `McpSseEndpointValidator`; `McpServerInfo`; `McpServerBootstrap`; `McpServerManagementTests`; `SecurityHardeningTests` | None | No |
| 5.5 Pro review P2 | MCP frontend still says saved server changes require restart | Closed | `McpServerConfig.vue` runtime-refresh wording; `SecurityHardeningTests` static guard | None | No |
| 5.5 Pro review P2 | Dapper failed-query logs include full SQL text | Closed | `DapperDatabaseConnector` logs SQL length/hash/provider/error type only; `TextToSqlReadOnlyTests`; `SecurityHardeningTests` | None | No |
| 5.5 Pro review P2 | Dapper truncation wording implies exact total row count | Closed | `DataAnalysisSqlQueryRunner`; `DataAnalysisAuditRecorder`; `SemanticAnalysisRunner`; `TextToSqlReadOnlyTests`; `SecurityHardeningTests` | None | No |
| 5.5 Pro review P2 | Document delete can leave orphan files if file deletion fails after DB save | Closed | PR #38; `DocumentFileDeletionRequestedEvent`; `DocumentFileDeletionRequestedConsumer`; `DeleteDocumentCommandHandler`; `RagUploadOutboxAtomicityTests`; `RagIndexingLifecycleTests`; `SecurityHardeningTests` | None | No |
| 5.5 Pro review P2 | Module repositories and frontend stores still contain structural duplication | Remaining | Repository templates, `configStore.ts`, and `ragStore.ts` remain maintainability backlog | Handle only after runtime safety fixes; do not mix with security PRs | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P2 | Bootstrap/admin secret operational discipline | Remaining | Deployment/runbook concern; no runtime code change in this closure | Add deployment-secret checklist/tests if deployment templates change | Not next unless deployment work starts |
| `REVIEW_FOLLOWUP_2026-04-29.md` P2 | API key in-memory zeroization | Deferred | Requires a broader secret-handling policy and may affect contracts | Track as compliance backlog | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P2 | AST comment-bypass concern | Not applicable | Current guardrails strip or inspect comments before SQL execution paths | Revisit only if SQL parser/query builder is replaced | No |
| `REVIEW_FOLLOWUP_2026-04-29.md` P2 | Vector key collision with non-integer document ids | Not applicable | Current document ids are integer database identities | Revisit only if document ids become GUID/string externally supplied values | No |
| `REVIEW_ACCEPTANCE_2026-05-07.md` remaining | Add RAG governance metadata editing for uploaded documents | Closed | `UpdateDocumentGovernanceCommand`; `DocumentGovernanceDrawer.vue`; `KnowledgeBaseManagement.vue`; RAG governance tests | None | No |
| `REVIEW_ACCEPTANCE_2026-05-07.md` remaining | Add Provider Reliability read-only configuration visibility in admin UI | Closed | `ProviderReliabilityConfig.vue`; provider reliability query/controller/tests | None | No |
| `REVIEW_ACCEPTANCE_2026-05-07.md` remaining | Workflow Graph/Planner | Deferred | Explicitly outside accepted scope | Product approval and separate design required | No |
| `REVIEW_ACCEPTANCE_2026-05-07.md` remaining | Long-term memory | Deferred | Explicitly outside accepted scope | Product approval and separate design required | No |
| `REVIEW_ACCEPTANCE_2026-05-07.md` remaining | Cloud write integrations | Deferred | Explicitly outside accepted scope and cross-project approval would be required | Product and cross-project approval required | No |
| `REVIEW_ACCEPTANCE_2026-05-07.md` remaining | OpenTelemetry/RabbitMQ/dashboard/private registry/deployment polish | Deferred | Operational/deployment backlog, not runtime correctness | Handle in a deployment hardening round | No |

## Recommended Next Implementation Round

Do not start another broad refactor from this ledger. PR #33 and PR #34 close the two remaining P1 runtime-safety findings from the 5.5 Pro review, and the MCP safety follow-up closes the two highest-signal P2 MCP findings.

1. Keep repository/store duplication as backlog unless a release or maintainability goal pulls it forward.
2. Final agent context/message consistency, deployment-secret hardening, Workflow Graph/Planner, long-term memory, and Cloud write integrations still require separate product or release plans.

Feature backlog items such as Workflow Graph/Planner, long-term memory, and Cloud write integrations should remain deferred until they receive explicit product and cross-project approval.
