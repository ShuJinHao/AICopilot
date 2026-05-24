# AICopilot Limited Pilot Authorization Template P17.9 Package

## Current Decision

- Stage: P17.9 authorization material template and blocking ledger.
- Default Go/No-Go: `BlockedByMissingAuthorizationMaterials`.
- Explicit execution request: missing.
- Signed approval: missing.
- Frozen Pilot Window evidence: missing.
- Credential configuration: not configured; responsibility placeholders only.
- Execution permission: not granted.
- Real Pilot execution: not allowed in P17.9.
- GA status: not GA.

## Human Authorization Template

| Section | Required Material | Current P17.9 Value |
| --- | --- | --- |
| Pilot Window | Name, start/end time, owner, approver, executor, rollback owner, emergency stop owner | TemplateRequired |
| Pilot users | 5-10 users, role, department, permission scope, approval status | TemplateRequired |
| Endpoints | Fixed allowlist | `devices`, `capacity_summary`, `device_logs`, `pass_station_records` |
| Data boundary | Last 7 days and default row cap | `maxRows=50` |
| Approval chain | Tool Approval and Final Approval | Required |
| Output boundary | Draft artifacts, final lock, hash-only operations ledger | Required |
| Credential responsibility | Configuration owner, custodian, approver, configuration window, rollback requirement | TemplateRequired |

## Blocking Ledger

Default open blockers:

- `MissingExplicitExecutionRequest`
- `MissingSignedApproval`
- `MissingFrozenPilotWindow`
- `MissingExecutorOrApprover`
- `MissingRollbackOrEmergencyStopOwner`
- `MissingCredentialWindow`
- `MissingDryRunEvidence`

Unsafe blocker to raise if detected:

- `UnsafeProductionBoundary`

## Go/No-Go Rules

- `BlockedByMissingAuthorizationMaterials`: one or more required authorization materials are missing.
- `BlockedByUnsafeBoundary`: Cloud write, Recipe/version, free SQL, real credential, raw payload, runtime rows, or secret exposure is found.
- `ReadyForUserMaterialSubmission`: template is complete and safe to hand to the user for manual filling, but execution remains forbidden.
- `ReadyForCredentialWindowPlanning`: only after future user-signed materials clear the blocking ledger; it does not configure credentials or execute a real Pilot.

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

P17.9 only makes the authorization material complete enough for manual submission. Real Pilot execution still requires a later explicit execution stage with signed approval, approved Pilot Window, approved credential configuration, executor confirmation, rollback window, emergency stop online verification, and post-execution audit archival.
