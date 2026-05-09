# AICopilot Claude Follow-up Closure

Date: 2026-05-08

Scope: AICopilot backend and AICopilot review records only. Cloud and Edge are out of scope.

This document closes the runtime safety, migration, session semantics, outbox, audit transaction, and structural duplication follow-up ledger after PR #27, PR #28, PR #29, PR #31, PR #33, PR #34, PR #39, and PR #40. The original Claude and 5.5 Pro reviews remain inputs, not the source of truth. Each item below is judged against the current `main` source and tests.

## Closure Matrix

| Area | Status | Evidence | Notes |
| --- | --- | --- | --- |
| Update user role revokes old sessions | Closed with behavior tests | `IdentityAccessManagementTests.UpdateUserRole_ShouldRevokeExistingSession`; `SecurityHardeningTests.UpdateUserRole_ShouldRefreshSecurityStamp` | Production already refreshes `SecurityStamp`; tests lock the old-token `401/session_revoked` behavior. |
| Local file storage path containment | Closed with hardening tests | `SecurityHardeningTests.LocalFileStorageService_ShouldConstrainAccessToConfiguredRoot`; `LocalFileStorageService_ShouldNotUseHardcodedDriveRoot` | Storage paths are constrained to configured roots; absolute and traversal paths are rejected. |
| Outbox multi-instance claim and cancellation semantics | Closed with static/behavior tests | `OutboxDispatcher`; `SecurityHardeningTests.OutboxDispatcher_ShouldUseSkipLockedAndNotRetryCancellation` | Dispatcher uses transactional `FOR UPDATE SKIP LOCKED`; cancellation is not counted as a delivery failure. |
| Chat workflow branch sink completion | Closed with behavior tests | PR #26; `ChatWorkflowOrchestrator`; `ClaudeFollowupClosureTests` | Branch sink completion now uses explicit async flow instead of detached `ContinueWith`, preserving flush and exception propagation. |
| JWT role claim authorization dependency | Closed with guard tests | `ClaudeFollowupClosureTests`; `IdentityAccessManagementTests` | Authorization remains permission-attribute based and session revocation is driven by `SecurityStamp`; `CurrentUser.Role` is audit/display data. |
| MCP argument parsing and SSE timeout | Closed with hardening tests | `McpServerBootstrap.ParseCommandArguments`; `McpRuntimeOptions`; `SecurityHardeningTests` | Command arguments are parsed structurally and runtime timeout options are bounded. |
| MCP disable/delete/config-change runtime convergence | Closed by PR #27 | `McpRuntimeRegistrySynchronizer`; `McpRuntimeRegistrySynchronizerTests`; `AgentPluginLoader` unregister tests | Future tool resolution converges after refresh. In-flight MCP calls are not force-killed by design. |
| RAG recoverable states and stale vector cleanup | Closed with lifecycle tests | `DocumentIndexingService`; `KnowledgeVectorIndexWriter`; `RagIndexingLifecycleTests`; `SecurityHardeningTests` | Re-indexing can recover eligible states and vector rebuilds remove stale chunk keys before upsert. |
| RAG upload and outbox atomicity | Closed by PR #33 | `RagIntegrationEventStager`; `RagUploadOutboxAtomicityTests`; `SecurityHardeningTests` | Document aggregate changes and `DocumentUploadedEvent` outbox rows are staged in `RagDbContext` and committed together. |
| Dapper read-only and row limit handling | Closed with hardening tests | `DapperDatabaseConnector`; data-analysis hardening tests | Row limits are enforced during data reading; only read-only query paths are allowed. |
| Identity migration ownership | Closed by PR #28 | `MigrationOwnershipTests`; split context snapshots | Current `AiCopilotDbContext` is guarded against re-owning Identity, RAG, MCP, DataAnalysis, and AiGateway tables. |
| Multi-DbContext migration history split | Closed by PR #28 | `MigrationHistoryTables`; `MigrationSafetyTests`; migration worker wiring | Split migration history tables are covered for fresh and legacy-bootstrap paths. |
| Approval policy and template/model long-session semantics | Closed by PR #29 | `SessionPolicySemanticsTests`; `ApprovalToolResolver`; `ApprovalDecisionValidator`; `FinalAgentContextSerializer`; `FinalAgentBuildExecutor` | Safety controls use current configuration. Existing sessions keep `session.TemplateId` but resolve the current template/model configuration; no snapshot schema was added. |
| Final agent context multi-instance storage | Closed for Redis-backed deployment baseline | `FinalAgentContextDeploymentTests`; `AcceptanceClosureVerificationTests.RedisFinalAgentContextStore_ShouldShareContextAcrossStoreInstances` | Distributed deployment requires Redis-backed context storage. Broader message/context transaction design remains separate backlog. |
| Cross-DbContext audit atomicity outside Identity | Closed by PR #31 | `AuditTransactionCoordinator`; `AuditTransactionBoundaryTests`; `SecurityHardeningTests` | Non-Identity business/config repositories commit business changes and staged audit rows in one EF transaction; audit save failure rolls back business changes. |
| RAG/MCP configuration and governance audit boundary | Closed by PR #34 | `RagRepository`; `McpServerRepository`; `RagMcpAuditCommandTests`; `AuditTransactionBoundaryTests`; `SecurityHardeningTests` | RAG/MCP write commands stage audit rows before repository save; sensitive API keys and MCP arguments are not recorded in audit summaries. |
| Repository and frontend store structural duplication | Closed by PR #39/#40 | `EfRepositoryBase`; `EfReadRepositoryBase`; `useConfigStore()` facade; `useRagStore()` facade; facade unit tests | Module repositories now share CRUD/spec/include infrastructure while preserving module bindings; config/RAG frontend domain logic is split behind stable facade stores. |

## Remaining Non-Runtime Items

The following items are intentionally not mixed into the runtime closure PRs:

| Item | Status | Reason |
| --- | --- | --- |
| Bootstrap/admin secret operational discipline | Remaining ops backlog | Current code avoids hardcoded production secrets, but deployment secret rotation and bootstrap runbook hardening are operational follow-up work. |
| API key in-memory zeroization | Deferred compliance backlog | Requires a broader secret-handling policy and may affect DTO/service contracts. |
| Workflow Graph/Planner, long-term memory, Cloud write integrations | Deferred product scope | These remain out of scope unless explicitly approved. |
| Template/model snapshot freeze for sessions | Deferred product/schema decision | PR #29 records the accepted current-config semantics; snapshot freeze would require schema changes and product agreement. |

## Current Result

- PR #27 closed MCP runtime reconciliation.
- PR #28 closed migration ownership and migration history guardrails.
- PR #29 closed approval/template long-session semantics.
- PR #31 closed non-Identity business/config audit transaction atomicity.
- PR #33 closed RAG upload and outbox atomicity.
- PR #34 closed RAG/MCP configuration and governance audit transaction coverage.
- PR #39 closed backend repository CRUD/spec/include duplication through shared EF repository bases.
- PR #40 closed frontend config/RAG store duplication through facade stores and domain modules.
- Remaining items are now tracked in `REVIEW_REMAINING_MATRIX_2026-05-08.md` instead of being treated as open-ended verbal follow-up.
