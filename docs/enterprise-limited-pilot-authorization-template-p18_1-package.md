# AICopilot Limited Pilot Fillable Authorization Template P18.1 Package

## Current Decision

- Stage: P18.1 fillable authorization template and offline submission validation.
- Default Go/No-Go: `BlockedNoSubmittedAuthorizationMaterials`.
- Submitted authorization materials: not received.
- Credential configuration: not configured; template responsibilities only.
- Execution permission: not granted.
- Real Pilot execution: not allowed in P18.1.
- GA status: not GA.

## Fillable Authorization Template

| Section | Required User Input | Validation |
| --- | --- | --- |
| Pilot Window | Name, start/end time, owner, approver, executor, rollback owner, emergency stop owner | All fields required |
| Pilot users | 5-10 users, role, department, permission scope, approval status | User count and approval status required |
| Endpoints | Fixed allowlist | `devices`, `capacity_summary`, `device_logs`, `pass_station_records` only |
| Data boundary | Last 7 days and default row cap | `maxRows=50` |
| Approval chain | Tool Approval and Final Approval | Both required |
| Output boundary | Draft artifacts, final lock, hash-only operations ledger | Must remain readonly evidence |
| Credential responsibility | Configuration owner, custodian, approver, configuration window, rollback requirement | Responsibility only; no real credential material |

## Offline Submission Validation

- No submitted material produces `BlockedNoSubmittedAuthorizationMaterials`.
- Missing Pilot Window, pilot users, approval chain, executor, rollback owner, emergency stop owner, endpoint, or data boundary produces `BlockedInvalidAuthorizationMaterials`.
- Endpoint outside `devices`, `capacity_summary`, `device_logs`, `pass_station_records` produces `BlockedInvalidAuthorizationMaterials`.
- Pilot user count outside 5-10, time range above last 7 days, or `maxRows` above 50 produces `BlockedInvalidAuthorizationMaterials`.
- Any real credential material, raw payload, runtime rows, full SQL, or sensitive context marker produces `BlockedUnsafeCredentialMaterial`.
- A complete safe submission only produces `ReadyForCredentialWindowPlanning`; it does not configure credentials or execute a real Pilot.

## Sample Set

### Safe Sample Shape

- Pilot Window fields: complete placeholders.
- Pilot users: 5 approved placeholder users.
- Endpoints: `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Data boundary: last 7 days and `maxRows=50`.
- Credential responsibility: owner, custodian, approver, configuration window, rollback requirement.
- Unsafe material markers: all false.

### Invalid Sample Shape

- Missing approver.
- Pilot users fewer than 5.
- Endpoint includes a forbidden Recipe/version value.
- `maxRows` above 50.

### Unsafe Sample Shape

- Credential material marker present.
- Raw payload marker present.
- Runtime row material marker present.
- Full SQL marker present.
- No real credential value or production payload is included in this package.

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

## Future Boundary

P18.1 only gives users a fillable authorization template and an offline validation package. Future submitted materials must still pass intake, credential-window planning, signed approval, approved credential configuration, executor confirmation, rollback window, emergency stop online verification, and post-execution audit archival before any real Pilot execution can be considered.
