# AICopilot M7 Dry-run Readiness Package

## Purpose

This package prepares M7 dry-run review material only. It is offline/simulated readiness work and is not real Pilot execution.

## Current Gate

- `ExecutionPermission=not granted`.
- `GateState=BlockedUntilExplicitM7Authorization`.
- Planning/readiness is not execution.
- GPT/5.5 Pro review is not M7 authorization.
- M7 remains hard-stopped until a later standalone approval provides the real business scope, endpoint/token handling, execution window, rollback owner, emergency owner, and evidence archive responsibility.

## Dry-run Checklist

- Pilot Authorization submission has complete M7 intake fields.
- Audit timeline contains only safe summary metadata.
- Sensitive content guard blocks token/API key/header/provider-key/JWT/private-key/database-url/raw-payload/raw-row/full-SQL/free-SQL/Chinese sensitive wording.
- Model/API pool productionization design is frozen without real secrets.
- RAG governance design is frozen without raw payload/raw rows/full SQL output.
- Enterprise data-source permission design is frozen with read-only, deny-by-default gates.
- Scope guard confirms no Cloud/Edge/frontend/appsettings diff.

## Explicit Non-goals

- No real Pilot execution.
- No real endpoint/token entry.
- No Cloud write.
- No Recipe/version access.
- No free SQL.
- No `query_cloud_data_readonly` enablement.
- No GA declaration.
