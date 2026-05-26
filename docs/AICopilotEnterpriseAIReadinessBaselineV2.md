# AICopilot Enterprise AI Readiness Baseline v2

Version: 2026-05-26

## Closure Conclusion

`M1, M2, M2.1, Batch 5-10, M5, M7 hard stop, and M7 dry-run readiness` are accepted as `AICopilot Enterprise AI Readiness Baseline v2`.

This is a readiness and governance closure after PR #55 was merged into `main` at merge commit `6c0b9cd`. It is not a completed enterprise AI platform, not real Pilot execution, not production rollout approval, and not GA.

## Baseline Scope

This baseline covers the current AICopilot governance and readiness foundation:

- M1 enterprise readiness baseline.
- M2 Pilot Authorization Workflow.
- M2.1 Pilot Authorization hardening.
- Batch 5-10 authorization observability and readiness.
- M5 enterprise data source platformization after PR #55.
- M7 hard stop package.
- M7 dry-run readiness package.

M3 model/API pool productionization, M4 RAG governance completion, and M6 security/compliance hardening remain follow-up productionization or hardening tracks. Their existing design and readiness materials remain useful evidence, but this v2 baseline does not claim those tracks are production-complete.

## M5 Boundary Accepted In v2

PR #55 closed the M5.1 raw SQL and Text-to-SQL permission boundary for the current readiness baseline:

- The raw readonly SQL API is retained only as a high-permission governed SQL operations API.
- Raw governed SQL requires `DataSource.QueryGovernedSql`, applies governed semantic schema checks, guardrails, query hash audit, and bounded sanitized preview.
- Text-to-SQL draft and draft-id execution require `DataSource.TextToSql`.
- Text-to-SQL execution must use a governed `DraftId`; raw SQL preview execution remains rejected.
- The default `User` role receives neither `DataSource.TextToSql` nor `DataSource.QueryGovernedSql`.
- Agent business database access stays on the Text-to-SQL path and selectable authorized `SimulationBusiness` sources.
- `CloudReadOnly` remains rejected for Agent and Text-to-SQL until a separately approved governed schema stage.

## Current Gate

- `ExecutionPermission=not granted`.
- `GateState=BlockedUntilExplicitM7Authorization`.
- Planning, readiness, and dry-run evidence do not grant execution permission.
- GPT/5.5 Pro review evidence does not grant M7 real Pilot authorization.
- Real Pilot requires a later standalone authorization with business scope, credential owner, execution window, rollback owner, emergency owner, endpoint/token handling, and audit archive responsibility.

## Frozen Boundaries

This closure does not change public APIs, backend behavior, frontend behavior, runtime behavior, persistence, migrations, scripts, appsettings, Cloud, or Edge.

The following remain forbidden in this baseline:

- Real Pilot execution.
- Real endpoint/token/API key/connection string configuration.
- `IIoT.CloudPlatform` or `IIoT.EdgeClient` modification.
- AICopilot frontend modification.
- Cloud write.
- Recipe or Recipe version access.
- Free SQL.
- `query_cloud_data_readonly` enablement.
- Raw payload, full SQL, raw business rows, token, API key, or connection string output.
- GA declaration.

## Accepted Meaning

This baseline means AICopilot has a stronger documented enterprise AI readiness foundation after the M5 data-source boundary fix entered `main`.

It does not mean M3, M4, or M6 are production-complete. It does not mean M7 real Pilot can start. It does not mean AICopilot is ready for Internal GA Candidate or GA.

## Next Stage Rule

All follow-up work must be split into separate tasks and review scopes. Any work involving real endpoint/token, real Pilot execution, Cloud write, Recipe/version, free SQL, `query_cloud_data_readonly` enablement, Cloud/Edge changes, frontend changes, or GA wording requires explicit standalone authorization before implementation.
