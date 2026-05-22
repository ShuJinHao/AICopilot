# AICopilot Limited Pilot Authorization P17.1 Package

## Purpose

This package prepares the internal authorization and dry-run rehearsal material for a future limited real-production readonly Pilot. It is an authorization artifact only. It does not execute the Pilot and does not configure runtime credentials.

## External Review Evidence

- 5.5 Pro review state is recorded as evidence only.
- `ReviewPending` does not block preparing authorization material.
- `BlockedByReview` must be carried forward as a risk before any future real execution.
- `NoBlocker` still does not grant execution permission by itself.

## Pilot Window Draft

- Pilot name: limited production readonly Pilot.
- Pilot scope: fixed readonly endpoints and bounded controlled analysis only.
- Start time: to be filled during the future approved Pilot Window.
- End time: to be filled during the future approved Pilot Window.
- Owner: to be assigned before execution.
- Approver: to be assigned before execution.
- Rollback owner: to be assigned before execution.
- Emergency stop owner: to be assigned before execution.
- Execution permission: not granted by this package.

## Pilot User Roster Template

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

Pilot user count target: 5-10 approved users.

## Endpoint Allowlist

Only the following endpoint codes are allowed for the future limited Pilot:

- `devices`
- `capacity_summary`
- `device_logs`
- `pass_station_records`

The following remain forbidden:

- Cloud write paths.
- Recipe and Recipe version.
- Unknown endpoint.
- Free SQL.
- Any endpoint outside the allowlist.

## Data Boundary

- Default time range: last 7 days.
- Default maxRows=50.
- Runtime rows policy: runtime-only, not persisted in operations ledger.
- Artifact data policy: approved, bounded, truncated, source-marked, and reviewed before final.
- Operations ledger policy: hash-only.
- Reports and frontends must not expose raw payload, raw business records, full SQL, token, API Key, connection string, or sensitive context.

## Dry-Run Rehearsal Checklist

Dry-run rules:

- No real token is used.
- No real endpoint is called.
- Fake/fixture inputs validate the full control chain.
- Dry-run output contains only endpoint, status, duration, row count, truncated state, approval status, query hash, and result hash.
- Dry-run output does not contain rows, raw payload, token, API Key, connection string, full SQL, or sensitive context.

Dry-run coverage:

- Pilot Window validation.
- Pilot user authorization template validation.
- Tool Approval before readonly query.
- Final Approval before final artifact state.
- Emergency stop activation and clearing rehearsal.
- Rollback rehearsal.
- Hash-only ledger verification.
- Source mode and boundary marker verification.

## Execution Runbook

Startup:

- Confirm Pilot Window is approved, active, and not expired.
- Confirm endpoint allowlist is exactly `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Confirm approved users are within the 5-10 user scope.
- Confirm Tool Approval and Final Approval are available.
- Confirm emergency stop owner and rollback owner are reachable.
- Confirm credential custody and configuration operator are recorded without storing secrets in the package.

Pause:

- Pause Pilot Window.
- Stop new Pilot tool execution.
- Preserve hash-only run ledger and approval evidence.

Emergency stop:

- Activate emergency stop.
- Confirm P12 fixed-template Pilot and P13 controlled Pilot execution are both blocked.
- Record operator, reason hash, timestamp, and audit reference.

Rollback:

- Disable the Pilot Window.
- Revoke or disable runtime credential configuration through the approved operator.
- Preserve hash-only evidence.
- Keep final artifacts locked and source-marked.

Evidence archive:

- Archive Pilot Window approval.
- Archive Tool Approval and Final Approval references.
- Archive endpoint, status, duration, row count, truncated state, approval status, query hash, and result hash.
- Do not archive rows, raw payload, token, API Key, connection string, full SQL, or sensitive context.

## Explicit Future Approval Requirement

Real Pilot execution requires a separate explicit user approval after this package. That approval must name the Pilot Window, approval chain, rollback strategy, credential configuration plan, execution owner, and emergency stop owner.
