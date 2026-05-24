# AICopilot M5 Enterprise Data Source Permission Design Freeze

## Goal

Freeze the M5 enterprise data source permission design while keeping all data access controlled, read-only, and gated.

## Required Capabilities

- Data-source visibility trimmed by role and explicit permission grant.
- Read-only query policy with allowlisted endpoint semantics only.
- Query-result limits aligned to Pilot Authorization constraints.
- Audit records that store safe summaries only.
- Deny-by-default behavior when scope, owner, time range, or permission material is missing.

## Security Boundary

- `query_cloud_data_readonly` remains disabled/hidden/non-executable unless a later standalone authorization explicitly opens it.
- Free SQL is not allowed.
- Cloud write is not allowed.
- Recipe/version is not allowed.
- Raw payload, raw rows, raw business rows, full SQL, endpoint URL, token, API key, and connection string output is not allowed.

## Frozen Items

- No Cloud/Edge changes.
- No real endpoint/token configuration.
- No real Pilot execution.
- No GA claim.
