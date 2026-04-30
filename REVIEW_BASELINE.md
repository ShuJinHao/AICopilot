# AICopilot Current Baseline Review Notes

Generated for the current large refactor baseline.

## Scope

- Only AICopilot is in scope.
- IIoT.CloudPlatform and IIoT.EdgeClient remain read-only references.
- This baseline does not add new business APIs, routes, or NuGet packages.

## Review Summary

- Query access is expected to go through Specifications, repositories, or dedicated read services. `IDataQueryService` must remain absent.
- `IReadRepository` no longer exposes `IQueryable`; query composition must stay inside infrastructure repositories, specifications, or dedicated read services.
- AiGateway, RAG, DataAnalysis, and MCP now use split business DbContexts on the same physical database with module schemas.
- HTTP authentication, upload limits, secret placeholders, ApiKey fail-fast behavior, and OpenTelemetry sensitive data gating are part of the security baseline.
- Identity runtime storage uses `IdentityStoreDbContext`; identity management commands use `ITransactionalExecutionService` and `IIdentityAuditLogWriter` so identity changes, permission sync, and identity audit rows share one EF transaction.
- Runtime and frontend usability closure has been merged: chat/approval, RAG knowledge management, DataAnalysis configuration feedback, MCP server management, and `/chat`, `/config`, `/knowledge`, `/access` frontend paths are part of the current baseline.
- Source directory casing is normalized under `src`, and core business aggregate IDs use strong typed identifiers internally while API DTOs and database columns remain Guid-based.

## Persistence Boundary

- `AiGatewayDbContext` owns `aigateway.language_models`, `aigateway.conversation_templates`, `aigateway.approval_policies`, `aigateway.sessions`, and `aigateway.messages`.
- `RagDbContext` owns `rag.embedding_models`, `rag.knowledge_bases`, `rag.documents`, and `rag.document_chunks`.
- `DataAnalysisDbContext` owns `dataanalysis.business_databases`.
- `McpServerDbContext` owns `mcp.mcp_server_info`.
- `IdentityStoreDbContext` owns runtime `identity.AspNet*` access. `AiCopilotDbContext` no longer maps Identity runtime entities.
- `IdentityStoreDbContext` has a snapshot-only baseline migration so future Identity schema changes have an explicit migration owner.
- Split-module migrations move existing public tables into module schemas when present and must not drop historical business data.

## Migration Notes

- `DetachIdentityFromAiCopilotDbContext` is snapshot-only; it must not contain `DropTable` or `CreateTable`.
- `MigrateIdentityKeysToGuid` is the previously approved destructive Identity key migration and still recreates `identity.AspNet*`; it discards old `public.AspNet*` Identity rows during active development.
- `MigrateIdentityKeysToGuid` must fail if existing `identity.AspNet*` tables contain rows. Running it against a database with real Identity rows in the `identity` schema requires explicit data-loss approval or a restore-tested upgrade path.
- `IdentityStoreMigrationBaseline` is snapshot-only and must not create, drop, or move physical tables.
- Split module migrations may remove old `public` tables only after data has been moved or copied into the owning module schema.
- MCP module schema migration uses guarded `ALTER TABLE ... SET SCHEMA` and must not silently merge duplicate `public.mcp_server_info` and `mcp.mcp_server_info` states.
- Fresh database verification must end with module tables in `aigateway`, `rag`, `dataanalysis`, `mcp`, Identity tables in `identity`, and Outbox in `outbox`.
- EF migration history is split per migration-owning DbContext. The migration runner bootstraps legacy rows from `public.__EFMigrationsHistory` into the per-context history tables before running migrations, and it fails fast if a target history table is partially copied.

## Audit Boundary

- `AiCopilotDbContext` is now the main infrastructure migration context for audit logs and Outbox; Identity runtime tables are detached from it.
- `IdentityStoreDbContext` owns runtime Identity stores and maps audit logs with `ExcludeFromMigrations()` for identity-management transaction atomicity.
- Runtime audit writing and audit querying use `AuditDbContext`.
- Audit writer decision tree:
  - Identity management commands use `IIdentityAuditLogWriter` only, inside `ITransactionalExecutionService`.
  - Ordinary business commands use `IAuditLogWriter`; repository save flushes business state first and then the scoped audit context.
  - Explicit `IAuditLogWriter.SaveChangesAsync` is allowed only for documented cross-DbContext execution paths that have no business save point.
  - New DbContexts must not map `AuditLogEntryConfiguration` unless the transaction boundary is reviewed and added to the architecture whitelist.
- AiGateway configuration commands stage business changes and audit rows, then commit through one repository save; the EF repository flushes the scoped `AuditDbContext` after the business context save.
- Identity commands do not inject `IAuditLogWriter` and do not call `auditLogWriter.SaveChangesAsync` directly. `EfTransactionalExecutionService` runs on `IdentityStoreDbContext`, and commits identity changes plus identity audit rows together.
- Explicit `auditLogWriter.SaveChangesAsync` calls are allowed only where there is no same business save point:
  - DataAnalysis writes that use `DataAnalysisDbContext` while regular runtime audit rows still use `AuditDbContext`.
  - Workflow executors that only audit read/execution activity and do not save a business aggregate.

## Outbox Boundary

- `outbox.outbox_messages` is the unified Outbox table for this baseline.
- Runtime Outbox dispatch and direct integration-event enqueueing use `OutboxDbContext`.
- Dispatcher horizontal scaling relies on PostgreSQL `FOR UPDATE SKIP LOCKED` while selecting due messages. Multiple dispatcher instances may poll concurrently without claiming the same row.
- Publish cancellation is not counted as a delivery failure and must not increment retry count; non-cancellation publish failures still use bounded retry and dead-letter behavior.
- Existing main migrations still own the current Outbox table history. This baseline does not add an Outbox migration.
- Module DbContexts may map Outbox with `ExcludeFromMigrations()` so they can stage events without owning the table.
- Business DbContexts keep their Outbox mappings so aggregate domain events can still be staged in the same transaction as business changes.

## Runtime Security And Operations Notes

- JWTs carry role and permission claims for request-time authorization. Identity governance changes that affect authorization, including role changes, disable/enable, and password reset, must refresh the user's security stamp so existing tokens are rejected on the next authenticated request.
- Login rate limiting is partitioned by normalized username plus client IP when the username can be read from the login request; it falls back to IP when username is absent or unreadable.
- Chat session reads, history, delete, normal chat execution, and pending approval lookup are scoped to the current user.
- New chat execution is blocked while the same session still has a pending approval context.
- MCP runtime registration is a startup-time operation. Disabling or changing an MCP server in the database requires service restart before the production chat toolchain reflects the change; hot unregister is intentionally not promised in this baseline.
- MCP SSE clients use an explicit connection timeout. Stdio MCP arguments use a quoted/escaped parser and still preserve the single-file-path shortcut.
- RAG indexing may recover documents left in `Parsing`, `Splitting`, or `Embedding`. Re-indexing loads existing chunks, deletes prior vector keys for the document, and then upserts the new vectors.
- RAG management includes embedding models, knowledge bases, document upload/status/delete, and knowledge-base search. Search requires the `Rag.SearchKnowledgeBase` permission.
- DataAnalysis configuration and execution paths must keep disabled-source, non-read-only-source, SQL safety rejection, and truncation feedback distinct.

## Explicitly Deferred

- Preview or 0.x package replacement, OpenTelemetry NU1902 remediation, RabbitMQ dedicated user, dashboard image pinning, private image registry, and Serilog adoption require dependency or operations decisions.
- Cross-DbContext audit atomicity outside Identity remains a future design item; this baseline does not introduce `TransactionScope` or distributed transactions.
- Cloud and edge alignment work remains out of scope unless explicitly requested.

## Required Verification

- `dotnet build .\AICopilot.slnx --no-restore`
- `npm run build` from `src/vues/AICopilot.Web`
- `dotnet test .\src\tests\AICopilot.ArchitectureTests\AICopilot.ArchitectureTests.csproj --no-build`
- `dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj --no-build`
- Static checks must keep `IDataQueryService`, repository queryable escape methods, Services/Core `IQueryable`, and Services/Core `IPublishEndpoint` at zero results.
- Static checks must keep `AddEntityFrameworkStores<AiCopilotDbContext>` out of production source, keep Identity management commands off `IAuditLogWriter`, and keep `DetachIdentityFromAiCopilotDbContext` free of `DropTable`/`CreateTable`.
