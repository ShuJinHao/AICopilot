# AICopilot Enterprise AI Known Gaps

Version: 2026-05-24

## Summary

The current baseline is acceptable as `AICopilot Enterprise AI Readiness Baseline v1`, but it is not a complete production-grade enterprise AI platform. The gaps below must remain visible so readiness closure is not mistaken for GA.

## M3 Model/API Pool Productionization

Known gaps:

- Production endpoint pool is not closed as a runtime capability.
- API key credential pool is not productionized.
- Per-endpoint and per-model concurrency limits are not proven under production load.
- Queue timeout, circuit breaker, fallback, health check, sticky streaming, token budget, cost budget, and per-user/per-role/per-tenant rate limits remain future work.
- Metrics dashboard and production operations evidence remain future work.

Required boundary:

- Do not configure real provider endpoint/token in repository files.
- Do not print or persist secrets in logs, audit, DTOs, reports, or artifacts.

## M4 RAG Governance Completion

Known gaps:

- Knowledge supplement default retrieval loop is not accepted as production-complete.
- Supplement priority, document version replacement, soft delete exclusion, physical delete background job, category permission, conflict handling, answer citation, and outdated document warning remain future work.
- RAG governance evidence remains planning/readiness material only.

Required boundary:

- Do not output raw payload, raw rows, raw business rows, full SQL, or sensitive context in RAG evidence.

## M5 Enterprise Data Source Platformization

Known gaps:

- Per-source permission grants are not accepted as production-complete.
- Schema sync, semantic schema, column allowlist/blocklist, sensitivity levels, data source audit, frontend source selector, agent source selector, and Text-to-SQL permission enforcement remain future work.
- Multi-source production governance is not closed.

Required boundary:

- Cloud write is not allowed.
- Recipe/version access is not allowed.
- Free SQL is not allowed.
- `query_cloud_data_readonly` remains disabled, hidden, and non-executable.

## M6 Security And Compliance Hardening

Known gaps:

- Enterprise DLP policy, secret scanning, artifact redaction, download audit, data retention, role matrix, approval matrix, prompt injection baseline guard, SQL injection hardening, malware scan hook, artifact safety scan hook, audit report export, and incident response runbook remain future work.
- Credential storage, rotation, vault/KMS integration, and secret lifecycle operations remain future work.

Required boundary:

- No token, API key, connection string, secret, raw payload, raw business row, or full SQL may be written to repository documents as evidence.

## M7 Real Pilot

Known gaps:

- Real Pilot is not authorized.
- Real Pilot is not executed.
- Real endpoint/token is not configured.
- Credential window and execution authorization binding require later standalone closure.
- Dry-run readiness is not execution.

Required boundary:

- `ExecutionPermission=not granted`.
- `GateState=BlockedUntilExplicitM7Authorization`.
- M7 remains hard-stopped until explicit standalone authorization is granted.

## GA And Internal GA Candidate

Known gaps:

- Internal GA Candidate is not reached.
- Formal GA is not allowed.
- SLA, support, monitoring, cost governance, security compliance, operational runbook, and audit reporting are not accepted as complete.

Required boundary:

- Do not describe this baseline as GA, production launch, or final completion of the enterprise AI platform.
