# AICopilot Limited Pilot Execution Authorization P17.3 Package

## Purpose

This package prepares a human execution authorization request for a future limited real-production readonly Pilot. It does not execute the Pilot and does not configure runtime credentials.

## Execution Authorization Request

- Authorization state: missing explicit user approval.
- Required future approval: named Pilot Window, approval chain, rollback strategy, credential configuration plan, execution owner, and emergency stop owner.
- Current decision without explicit approval: `MissingAuthorization`.
- Possible decision after complete material and separate user approval: `ReadyForExplicitExecutionApproval`.

## Pilot Window Draft

- Pilot name: limited production readonly Pilot.
- Start time: to be filled during the future approved Pilot Window.
- End time: to be filled during the future approved Pilot Window.
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
- Runtime rows policy: runtime-only, not persisted in operations ledger.
- Operations ledger policy: hash-only.

## Approval Chain

- Pilot Window approval is required before any future execution.
- Tool Approval is required before readonly query.
- Final Approval is required before final artifact state.
- Emergency stop owner must be assigned and reachable.
- Rollback owner must be assigned and reachable.

## Credential Readiness Preflight

- Credential configuration owner: to be assigned before execution.
- Credential custodian: to be assigned before execution.
- Credential approver: to be assigned before execution.
- Credential status placeholder: configured/not-configured only.
- No real token is written, read, displayed, or validated in P17.3.
- No API Key or connection string is written, read, displayed, or validated in P17.3.
- Real credential configuration is allowed only in a later stage after explicit user execution approval.

## Go/No-Go Rules

- `MissingAuthorization`: explicit user execution approval is missing.
- `MissingCredentialPlan`: credential responsibility or configuration plan is missing.
- `BlockedByDryRunFailure`: P17.2 dry-run evidence is incomplete or unsafe.
- `ReadyForExplicitExecutionApproval`: material is complete, dry-run evidence is complete, and explicit user execution approval is recorded; this is still an approval request state, not execution.

## Evidence Requirements

- P17.2 dry-run evidence covers fixed-template and controlled-goal paths.
- P17.2 dry-run evidence covers `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- P17.2 refusal evidence covers Recipe, Recipe version, Cloud write, unknown endpoint, over maxRows, and over time range.
- P17.2 emergency stop evidence proves active stop rejects both Pilot paths.
- P17.2 rollback evidence proves no real credential is touched.

## Future Execution Boundary

P17.3 success does not grant real Pilot execution. Real execution still requires a later explicit approval naming Pilot Window, approval chain, rollback strategy, credential configuration plan, execution owner, and emergency stop owner.
