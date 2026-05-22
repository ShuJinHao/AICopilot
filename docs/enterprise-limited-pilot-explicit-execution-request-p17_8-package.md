# AICopilot Limited Pilot Explicit Execution Request P17.8 Package

## Current Decision

- Stage: P17.8 explicit manual execution request intake and final pre-execution gate.
- Default request state: `BlockedNoExplicitExecutionRequest`.
- Explicit execution request: not received.
- Signed approval evidence: missing.
- Frozen Pilot Window evidence: missing.
- Execution permission: not granted.
- Credential status: not configured; responsibility placeholders only.
- Real Pilot execution: not allowed in P17.8.
- GA status: not GA.

## Explicit Manual Execution Request

| Item | Required For Future Stage | Current P17.8 Value |
| --- | --- | --- |
| Explicit user execution request | Required | Not received |
| Signed execution approval | Required | Missing |
| Frozen Pilot Window evidence | Required | Missing |
| Executor | Required | ToBeSigned |
| Approval chain | Required | ToBeSigned |
| Rollback owner | Required | ToBeSigned |
| Emergency stop owner | Required | ToBeSigned |
| Pilot users | 5-10 users | ToBeSigned |
| Endpoint allowlist | Fixed allowlist | `devices`, `capacity_summary`, `device_logs`, `pass_station_records` |
| Time range | Fixed boundary | Last 7 days |
| `maxRows` | Fixed default | `maxRows=50` |
| Tool Approval | Required | Required |
| Final Approval | Required | Required |
| Operations ledger | Required | hash-only |

## Credential Window Preflight

P17.8 only records credential configuration responsibility, custody, approver, and future configuration window placeholders.

It does not write, read, display, test, or validate real token, API key, connection string, or endpoint credential values. Real credential handling can only be considered in a later explicitly approved execution stage.

## Go/No-Go Rules

- `BlockedNoExplicitExecutionRequest`: no explicit user execution request has been received.
- `BlockedSignedApprovalMissing`: signed approval or frozen Pilot Window evidence is missing.
- `BlockedExecutionRequestIncomplete`: executor, approval chain, rollback owner, emergency stop owner, endpoint allowlist, or data boundary is incomplete.
- `BlockedCredentialWindowMissing`: credential configuration window or custody responsibility is incomplete.
- `ReadyForCredentialConfigurationWindow`: all materials are complete, but this only permits a later credential configuration window preparation. It does not execute a real Pilot.

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

Even if a future package reaches `ReadyForCredentialConfigurationWindow`, real Pilot execution still requires a separate stage with an explicit user execution instruction, signed approval, approved Pilot Window, approved credential configuration, executor confirmation, rollback window, emergency stop online verification, and post-execution audit archival.
