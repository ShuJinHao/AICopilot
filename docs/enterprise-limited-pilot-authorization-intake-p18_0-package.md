# AICopilot Limited Pilot Authorization Intake P18.0 Package

## Current Decision

- Stage: P18.0 authorization material intake and credential-window preparation.
- Default Go/No-Go: `BlockedNoSubmittedAuthorizationMaterials`.
- Submitted authorization materials: not received.
- Credential window: not configured; planning placeholders only.
- Execution permission: not granted.
- Real Pilot execution: not allowed in P18.0.
- GA status: not GA.

## Submitted Authorization Material Requirements

| Section | Required Material | Current P18.0 Value |
| --- | --- | --- |
| Pilot Window | Name, start/end time, owner, approver, executor, rollback owner, emergency stop owner | NotSubmitted |
| Pilot users | 5-10 users, role, department, permission scope, approval status | NotSubmitted |
| Endpoints | Fixed allowlist | `devices`, `capacity_summary`, `device_logs`, `pass_station_records` |
| Data boundary | Last 7 days and default row cap | `maxRows=50` |
| Approval chain | Tool Approval and Final Approval | Required |
| Output boundary | Draft artifacts, final lock, hash-only operations ledger | Required |
| Credential responsibility | Configuration owner, custodian, approver, configuration window, rollback requirement | NotSubmitted |

## Credential Window Preparation

P18.0 may record:

- Configuration owner.
- Credential custodian.
- Credential approval owner.
- Configuration window.
- Rollback requirement.
- Emergency stop online verification requirement.

P18.0 must not store, read, validate, test, or display a real token, API key, connection string, raw payload, runtime rows, full SQL, or sensitive context.

## Validation Rules

- Missing submitted materials keep the stage in `BlockedNoSubmittedAuthorizationMaterials`.
- Missing required fields, endpoint overflow, pilot user count outside 5-10, time range over last 7 days, or `maxRows` over 50 produce `BlockedInvalidAuthorizationMaterials`.
- Any real credential material, raw payload, runtime rows, full SQL, or sensitive context produce `BlockedUnsafeCredentialMaterial`.
- Complete and safe materials only produce `ReadyForCredentialWindowPlanning`; this is still planning-only and does not execute a real Pilot.

## Forbidden Range

- No Cloud write.
- No Recipe or Recipe version.
- No free SQL.
- No arbitrary production endpoint.
- No unauthorized user.
- No unapproved final output.
- No rows or raw payload in operations reports.
- No token, API key, connection string, full SQL, or sensitive context output.
- `query_cloud_data_readonly` remains disabled, hidden, and non-executable.

## Future Execution Boundary

P18.0 only determines whether authorization materials are complete enough to plan a credential configuration window. Real Pilot execution still requires a later explicit execution stage with signed approval, approved Pilot Window, approved credential configuration, executor confirmation, rollback window, emergency stop online verification, and post-execution audit archival.
