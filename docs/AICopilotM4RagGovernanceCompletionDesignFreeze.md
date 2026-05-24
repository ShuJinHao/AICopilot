# AICopilot M4 RAG Governance Completion Design Freeze

## Goal

Freeze the M4 RAG governance completion design before any M7 execution work.

## Required Capabilities

- Source registry for every indexed document or supplement.
- Version and category metadata for governed knowledge.
- Disable/delete lifecycle that removes disabled content from recall.
- Recall audit that records safe summary fields only.
- Evidence package that references governed artifacts without embedding raw business rows or raw payload.

## Output Boundary

- Do not output raw payload, raw rows, raw business rows, full SQL, free SQL, tokens, API keys, connection strings, or provider responses.
- RAG evidence remains a planning/readiness artifact and does not authorize execution.

## Frozen Items

- No real Pilot execution.
- No Cloud write.
- No Recipe/version expansion.
- No AICopilot frontend changes.
- No GA claim.
