# AICopilot Post-Baseline v2 Roadmap

Version: 2026-05-26

## Roadmap Rule

After `AICopilot Enterprise AI Readiness Baseline v2`, follow-up work must be split into separate tasks and review scopes. No later work may infer real Pilot authorization, endpoint/token approval, Cloud/Edge modification approval, frontend approval, or GA approval from this baseline closure.

All roadmap items default to AICopilot-only unless a later task explicitly authorizes cross-project work.

## Track 1: M4 RAG Governance Loop

Goal: make RAG governance affect default retrieval and answer evidence.

Minimum focus:

- Knowledge supplement default retrieval.
- Supplement priority over older documents.
- Version replacement and soft delete exclusion.
- Category permission.
- Conflict handling.
- Answer citation and outdated document warning.

Boundary:

- No raw payload, raw rows, raw business rows, full SQL, or sensitive context output.
- No Cloud/Edge changes unless separately authorized.

## Track 2: M3 Model/API Pool Productionization

Goal: productionize enterprise model and API usage for concurrent employees.

Minimum focus:

- Model endpoint pool.
- API key credential pool.
- Per-endpoint and per-model concurrency limits.
- Queue timeout, circuit breaker, fallback, health check, sticky streaming, token budget, cost budget, and rate limits.
- Operational metrics.

Boundary:

- No real provider endpoint/token committed to repository files.
- No secret output.
- No Cloud/Edge changes.

## Track 3: M6 Security And Compliance Hardening

Goal: close the enterprise security, compliance, audit, and retention baseline.

Minimum focus:

- DLP policy.
- Secret scanning and artifact redaction.
- Download audit and data retention.
- Role matrix and approval matrix.
- Prompt injection and SQL injection guard hardening.
- Upload malware scan hook and artifact safety scan hook.
- Audit report export and incident response runbook.

Boundary:

- No plaintext secrets in repository, logs, audit, DTOs, reports, or artifacts.
- No runtime relaxation of M5 governed SQL or Text-to-SQL boundaries.

## Track 4: M7 Dry-run

Goal: rehearse the real Pilot control chain without connecting to real endpoint/token or executing real Pilot.

Minimum focus:

- Pilot Authorization submission.
- Machine validation.
- Review and planning approval flow.
- `ExecutionPermission=not granted`.
- `GateState=BlockedUntilExplicitM7Authorization`.
- Emergency stop drill.
- Rollback drill.
- Audit archive rehearsal.

Boundary:

- No real Pilot execution.
- No real endpoint/token.
- No Cloud write.
- No `query_cloud_data_readonly` enablement.
- No GA.

## Track 5: M7 Limited Real Pilot

Goal: run a small real Pilot only after standalone explicit authorization and completion of required prerequisites.

Entry requirements:

- Readiness Baseline v2 closed.
- M2/M7 authorization binding remains enforced.
- M3 minimum production model/API pool capability is closed.
- M4 RAG governance loop is closed.
- M5 raw governed SQL and Text-to-SQL permission boundary remains enforced.
- M6 security/compliance baseline is closed.
- M7 dry-run passed.
- Standalone real Pilot authorization is granted.

Boundary:

- Scope, endpoints, credential handling, execution window, rollback owner, emergency owner, and audit archive must be explicitly approved before implementation.

## Track 6: Internal GA Candidate

Goal: evaluate whether AICopilot can become an internal GA candidate after limited Pilot evidence and operational hardening.

Entry requirements:

- Limited Pilot evidence accepted.
- Security, compliance, SLA, monitoring, audit reporting, cost governance, and support runbook accepted.
- Known gaps either closed or explicitly accepted with owner and expiry.

Boundary:

- Internal GA Candidate is not formal GA.
- Formal GA requires a later standalone approval.
