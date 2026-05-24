# AICopilot M3 Model/API Pool Productionization Design Freeze

## Goal

Freeze the M3 productionization design for model and API pools without connecting real provider endpoints or storing secrets.

## Required Capabilities

- Model/provider pool routing with explicit priority, health, and fallback policy.
- Per-provider and per-model rate limits.
- Circuit-breaker state that can be observed without exposing endpoint URLs or secret values.
- Budget controls for request count, token estimate, output token ceiling, and daily spend envelope.
- Degraded-mode behavior that falls back to safe planning/readiness output when provider health is poor.

## Security Boundary

- Secrets must come only from configuration providers or environment variables at runtime.
- Secret values, provider tokens, API keys, endpoint URLs, connection strings, request payloads, and raw responses must not be written to code, docs, logs, reports, DTOs, audits, or test snapshots.
- M3 design does not grant Pilot execution and does not change `ExecutionPermission=not granted`.

## Frozen Items

- No real provider endpoint/token is configured in this PR.
- No Cloud/Edge changes.
- No AICopilot frontend changes.
- No GA claim.
