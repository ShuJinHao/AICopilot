# AICopilot Limited Pilot Signed Approval P17.5 Package

## Purpose

This package prepares a human-signable execution approval record for a future limited real-production readonly Pilot. It does not execute the Pilot and does not configure runtime credentials.

## Signed Execution Approval State

- Default decision: `MissingSignedExecutionApproval`.
- Incomplete decision: `SignedApprovalIncomplete`.
- Credential window incomplete decision: `CredentialWindowIncomplete`.
- Complete planning decision: `ReadyForManualPilotExecutionStep`.
- Current execution permission: not granted.

## Pilot Window Approval Template

- Pilot name: limited production readonly Pilot.
- Start time: to be signed before execution.
- End time: to be signed before execution.
- Owner: to be assigned before execution.
- Approver: to be assigned before execution.
- Executor: to be assigned before execution.
- Rollback owner: to be assigned before execution.
- Emergency stop owner: to be assigned before execution.
- Execution permission: not granted by this package.

## Pilot User Scope

Pilot user count target: 5-10 approved users.

| User | Role | Department | Permission Scope | Approval Status |
| --- | --- | --- | --- | --- |
| pilot-user-01 | Pilot User | To be assigned | Readonly Pilot templates | Pending |
| pilot-user-02 | Pilot User | To be assigned | Readonly Pilot templates | Pending |
| pilot-user-03 | Pilot User | To be assigned | Readonly Pilot templates | Pending |
| pilot-user-04 | Pilot User | To be assigned | Readonly Pilot templates | Pending |
| pilot-user-05 | Pilot User | To be assigned | Readonly Pilot templates | Pending |
| pilot-user-06 | Optional Pilot User | To be assigned | Readonly Pilot templates | Pending |
| pilot-user-07 | Optional Pilot User | To be assigned | Readonly Pilot templates | Pending |
| pilot-user-08 | Optional Pilot User | To be assigned | Readonly Pilot templates | Pending |
| pilot-user-09 | Optional Pilot User | To be assigned | Readonly Pilot templates | Pending |
| pilot-user-10 | Optional Pilot User | To be assigned | Readonly Pilot templates | Pending |

## Endpoint And Data Boundary

- Endpoint allowlist: `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Default time range: last 7 days.
- Default maxRows=50.
- Forbidden: Cloud write, Recipe/version, unknown endpoint, free SQL, unapproved final state, unauthorized users.
- Runtime business row data policy: runtime-only.
- Operations ledger policy: hash-only.

## Output Boundary

- Draft artifacts are allowed only after the future approved execution stage performs the readonly query under gate, window, approval, and emergency stop controls.
- Tool Approval is required before any future readonly query.
- Final artifacts require Final Approval.
- Final artifacts must be locked after approval.
- Operations ledger remains hash-only.

## Credential Configuration Window

- Configuration owner: to be assigned before execution.
- Custodian: to be assigned before execution.
- Approver: to be assigned before execution.
- Configuration window: to be signed before execution.
- Rollback requirement: disabling the window and revoking the future runtime configuration path.
- Real credential read/write/display: forbidden in P17.5.
- Real credential configuration is allowed only in a later explicit execution stage after separate user approval.

## Go/No-Go Rules

- `MissingSignedExecutionApproval`: no signed human approval is recorded.
- `SignedApprovalIncomplete`: signed material is missing Pilot Window, approver, executor, rollback owner, emergency stop owner, endpoint allowlist, or data boundary.
- `CredentialWindowIncomplete`: credential configuration responsibility or configuration window is incomplete.
- `ReadyForManualPilotExecutionStep`: signed material is complete and may be handed to a later manual execution step; this is not execution.

## Future Execution Boundary

P17.5 success does not grant real Pilot execution. Real execution still requires a later explicit user instruction, approved Pilot Window, approved credential configuration, executor confirmation, rollback window, emergency stop online verification, and post-execution audit archival.
