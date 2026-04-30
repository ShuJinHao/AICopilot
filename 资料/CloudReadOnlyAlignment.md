# AICopilot Cloud Read-only Alignment

## Purpose

This document defines the current preparation boundary for aligning `AICopilot` with `IIoT.CloudPlatform`.

`AICopilot` may use Cloud business data only as read-only analysis input. It may explain, summarize, search, compare, diagnose, and generate suggestions. It must not create, update, delete, backfill, approve, dispatch, or trigger Cloud business records or workflows.

This is a planning and integration-boundary document. It does not introduce Cloud APIs, AICopilot tools, database migrations, DTO changes, or frontend protocol changes.

## Non-goals

- No direct Cloud database writes from AICopilot.
- No direct references from AICopilot projects to Cloud implementation projects.
- No MCP, Tool, Agent workflow, background job, SQL script, or hidden adapter that indirectly calls Cloud write APIs.
- No use of Human-in-the-loop approval to bypass the Cloud business write ban.
- No implementation of Cloud AI-facing write APIs in this phase.

## Current Cloud Read Data Inventory

The following Cloud data categories are suitable for read-only analysis after the Cloud side provides an approved read path, read-only database source, exported document, or explicit read-only API.

| Category | Current Cloud meaning | Suitable AICopilot use | Forbidden AICopilot use |
| --- | --- | --- | --- |
| Employees and permissions | Employee records, access, roles, permissions | Explain access state, inspect permission gaps, summarize onboarding/access issues | Create employees, disable users, change roles, assign permissions |
| Devices | Device master data, device identity, bootstrap relationship | Explain device status, detect missing metadata, correlate with capacity/log data | Register devices, delete devices, rotate bootstrap secret, edit device profile |
| Recipes and processes | Process master data, recipe versions, active recipe relationship | Explain recipe version history, compare parameters, answer process/recipe questions | Create/delete recipes, upgrade recipe versions, modify process master data |
| Capacity | Hourly, daily, summary, and range capacity records | Trend analysis, anomaly explanation, report generation | Backfill, correct, delete, or manually submit capacity records |
| Device logs | Device log records by level, keyword, date, and time range | Troubleshooting, summarization, incident correlation | Insert, rewrite, suppress, or delete logs |
| Pass station / production data | Production and pass-station records by configured type | Query, inspect, summarize, and explain production flow records | Upload, correct, delete, or re-route production records |
| Deployment and rule documents | Business rules, status-flow rules, terminology, deployment notes | RAG retrieval, rule explanation, consistency checks | Treat document suggestions as executed business changes |

Edge upload endpoints and internal persistence commands are not AICopilot integration targets. They exist for Edge and Cloud internals, not for AI tool execution.

## RAG Input Plan

RAG is suitable for documents and stable knowledge:

- Root business-rule documents under `docs/`, including business rules, terminology, status-flow rules, and cross-project alignment rules.
- Cloud read-only API descriptions after they are explicitly written.
- Deployment and operations runbooks that do not expose secrets.
- Troubleshooting notes that have been reviewed for sensitive data.

RAG must not ingest secrets, connection strings, raw credential material, private keys, unredacted personal-sensitive exports, or production database dumps.

RAG answers are explanatory. They do not become Cloud business writes.

## DataAnalysis Read-only Plan

DataAnalysis may query Cloud data only through read-only sources:

- Prefer read-only replicas, reporting views, or dedicated read-only database users.
- Keep SQL guardrails enabled.
- Keep read-only database session enforcement enabled.
- Keep `MaxRows` limits enforced.
- Prefer narrow reporting views over direct broad table access.
- Do not grant AICopilot write-capable connection strings.

DataAnalysis output may be used for charts, summaries, diagnostics, and recommendations. It must not be used to mutate Cloud records.

## Future Cloud AI-facing API Reservation

The current default is: no Cloud AI-facing write API exists.

If Cloud later exposes an AI-facing API, the API must be designed and owned by `IIoT.CloudPlatform`, not inferred by AICopilot. Before AICopilot can call it, the following must be explicit:

- The API is intentionally exposed for AI usage.
- The API contract, permissions, audit fields, rate limits, and error semantics are documented.
- The allowed action list is finite and reviewed.
- Read-only APIs are separated from write-capable APIs.
- Write-capable APIs require a separate design decision and are not enabled by this document.

Until that design exists, AICopilot may only use Cloud read paths.

## MCP And Tool Boundary

Future Cloud-related MCP tools must default to query-only behavior.

Allowed tool semantics:

- `get`
- `list`
- `query`
- `search`
- `summarize`
- `explain`
- `analyze`

Forbidden tool semantics for Cloud business data:

- `create`
- `update`
- `delete`
- `register`
- `disable`
- `approve`
- `dispatch`
- `trigger`
- `backfill`
- `correct`
- `upload`
- `submit`

Tool names, descriptions, permissions, and audit summaries must state that the tool is read-only when it touches Cloud data.

## Readiness Checklist

Before any AICopilot-to-Cloud integration PR:

- The integration is read-only, or the user has explicitly approved a separate Cloud AI-facing API design.
- AICopilot does not reference Cloud implementation projects directly.
- The data source is a read-only API, read-only database user, read-only view, exported document, or approved file.
- The integration cannot indirectly call Cloud write APIs through MCP, workflow, tool adapters, or background jobs.
- The audit text describes analysis/query behavior, not business mutation.
- The UI copy does not imply that AICopilot can execute Cloud business changes.
- Tests or static guards cover the no-direct-Cloud-reference rule.

## Open Follow-up

The Cloud side still needs a dedicated read-only AI integration design before implementation. Candidate work includes:

- Decide which Cloud read APIs should be exposed specifically to AICopilot.
- Define read-only permission names and audit categories.
- Define read-only reporting views for DataAnalysis.
- Decide which business documents should be indexed into RAG.
- Define a Cloud-owned AI-facing API convention before any write-capable action is even considered.
