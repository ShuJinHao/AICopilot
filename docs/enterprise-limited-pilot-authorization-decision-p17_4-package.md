# AICopilot Limited Pilot Authorization Decision P17.4 Package

## Purpose

This package records the human authorization decision state and freezes the execution planning window for a future limited real-production readonly Pilot. It does not execute the Pilot and does not configure runtime credentials.

## Authorization Decision Record

- Default decision: `AuthorizationPending`.
- Rejected decision: `AuthorizationRejected`.
- Planning-only approval decision: `AuthorizationGrantedForPlanning`.
- Current execution permission: not granted.
- External review state: evidence only, not a blocking condition for P17.4 material preparation.

## Pilot Window Freeze Draft

- Pilot name: limited production readonly Pilot.
- Start time: to be approved before execution.
- End time: to be approved before execution.
- Owner: to be assigned before execution.
- Approver: to be assigned before execution.
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

## Approval Chain Freeze

- Pilot Window approval is required before any future execution.
- Tool Approval is required before readonly query.
- Final Approval is required before final artifact state.
- Emergency stop owner must be assigned and reachable before execution.
- Rollback owner must be assigned and reachable before execution.

## Credential Responsibility Freeze

- Configuration owner: to be assigned before execution.
- Custodian: to be assigned before execution.
- Approver: to be assigned before execution.
- Credential status placeholder: configured/not-configured only.
- Real credential read/write/display: forbidden in P17.4.
- Real credential configuration is allowed only in a later explicit execution stage after separate user approval.

## Go/No-Go Rules

- `AuthorizationPending`: no explicit user authorization is recorded; execution remains unavailable.
- `AuthorizationRejected`: user rejected or postponed authorization; execution remains unavailable.
- `WindowFreezeIncomplete`: Pilot Window, approval chain, rollback owner, emergency stop owner, user scope, endpoint allowlist, or data boundary is incomplete.
- `CredentialResponsibilityIncomplete`: credential responsibility and placeholder-state plan is incomplete.
- `AuthorizationGrantedForPlanning`: user explicitly approved execution planning material; this is not permission to query a real endpoint.

## Future Execution Boundary

P17.4 success does not grant real Pilot execution. Real execution still requires a later explicit approval naming the Pilot Window, approval chain, runtime credential configuration confirmation, rollback window, emergency stop validation, and execution owner.
