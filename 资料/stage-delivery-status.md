# AICopilot Stage Delivery Status

Updated: 2026-04-30

## Current State

- PR #1 has been merged into `main` as the persistence, migration, and security baseline.
- PR #2 has been merged into `main` as the runtime and frontend usability closure.
- The active user-facing paths are `/chat`, `/config`, `/knowledge`, and `/access`.
- The tracked worktree should stay clean except for intentionally ignored local review artifacts.

## Completed Capabilities

- Identity, roles, permissions, audit query, and JWT security-stamp invalidation are in place.
- Chat sessions are scoped to the current user, and pending approvals block conflicting chat execution in the same session.
- Human approval state is visible in the chat UI and recovers across refresh and session switching.
- RAG supports embedding model management, knowledge base management, document upload, indexing status, retryable indexing, vector cleanup, and search.
- DataAnalysis keeps read-only query execution and shows clearer configuration/runtime safety feedback.
- MCP server configuration can be managed from the UI; runtime bootstrap changes still require service restart.

## Cleanup Decisions

- `IReadRepository.GetQueryable()` was removed because no production or test code uses it and it leaks `IQueryable` outside infrastructure.
- Old PR planning drafts were removed because they described pre-merge state and contradicted the current GitHub state.
- Active runtime implementations are not cleanup targets: `Infrastructure/Mcp/McpServerBootstrap`, RAG parser/tokenizer/text splitter, and `DataAnalysisOutputDto` are still used.

## Deferred Work

- Directory casing normalization.
- Strongly typed IDs.
- Per-DbContext `__EFMigrationsHistory` split.
- Preview/0.x package replacement and operations dependency decisions.
- Cloud/Edge alignment work, unless explicitly requested.
