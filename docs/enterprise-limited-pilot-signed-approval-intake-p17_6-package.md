# AICopilot Limited Pilot Signed Approval Intake P17.6 Package

## Purpose

This package records signed approval intake and the routing decision for a future limited production readonly Pilot. It does not execute the Pilot and does not configure runtime credentials.

## Intake State

- Default intake state: `NoSignedApprovalReceived`.
- Incomplete signed material state: `SignedApprovalIncomplete`.
- Incomplete credential window state: `CredentialWindowIncomplete`.
- Complete planning state: `ReadyForManualExecutionStepPlanning`.
- Current execution permission: not granted.

## Intake Evidence Table

| Field | Required Value | Current Placeholder |
| --- | --- | --- |
| Signed approval received | true before future execution | false |
| Pilot Window signed | true before future execution | false |
| Approver assigned | true before future execution | false |
| Executor assigned | true before future execution | false |
| Rollback owner assigned | true before future execution | false |
| Emergency stop owner assigned | true before future execution | false |
| Endpoint allowlist approved | true before future execution | false |
| Data boundary approved | true before future execution | false |
| Credential window approved | true before future execution | false |

## Manual Execution Step Checklist

- Pilot name: limited production readonly Pilot.
- Pilot Window: to be supplied by signed material before execution.
- Owner: to be supplied by signed material before execution.
- Approver: to be supplied by signed material before execution.
- Executor: to be supplied by signed material before execution.
- Rollback owner: to be supplied by signed material before execution.
- Emergency stop owner: to be supplied by signed material before execution.
- Pilot user count target: 5-10 approved users.
- Endpoint allowlist: `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Default time range: last 7 days.
- Default maxRows=50.
- Tool Approval is required before any future readonly query.
- Final Approval is required before any future final artifact.
- Operations ledger remains hash-only.

## Credential Window

- Configuration owner: to be supplied by signed material before execution.
- Custodian: to be supplied by signed material before execution.
- Approver: to be supplied by signed material before execution.
- Configuration window: to be supplied by signed material before execution.
- Rollback requirement: disable the future execution window and revoke the future runtime configuration path.
- Real credential read/write/display: forbidden in P17.6.

## Routing Rules

- `NoSignedApprovalReceived`: keep the system non-executable.
- `SignedApprovalIncomplete`: keep the system non-executable and request corrected signed material.
- `CredentialWindowIncomplete`: keep the system non-executable and request corrected credential responsibility material.
- `ReadyForManualExecutionStepPlanning`: only allows planning the next manual execution step; it does not call a real endpoint.

## Future Execution Boundary

P17.6 success does not grant real Pilot execution. Real execution still requires a separate user instruction, approved Pilot Window, approved credential configuration, executor confirmation, rollback window, emergency stop online verification, and post-execution audit archival.

