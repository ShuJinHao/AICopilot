# AICopilot P17.9 Scope Freeze: Authorization Template And Blocking Ledger

## Purpose

P17.9 freezes a human-fillable authorization material template and a blocking ledger for the limited production readonly Pilot readiness track.

P17.9 does not execute a real Pilot, does not configure credentials, does not call a real endpoint, does not generate real production artifacts, and is not GA.

## Allowed Scope

- Modify only AICopilot documentation, reports, acceptance scripts, and readonly diagnostic material.
- Preserve P17.8 explicit manual execution request evidence.
- Consolidate Pilot Window, signed approval, explicit request, credential responsibility, rollback, emergency stop, and dry-run evidence requirements into one authorization template.
- Record missing material and unsafe boundary blockers without clearing them automatically.

## Frozen Areas

- IIoT.CloudPlatform is frozen.
- IIoT.EdgeClient is frozen.
- Cloud write is forbidden.
- Recipe and Recipe version are forbidden.
- Free SQL and arbitrary endpoint payloads are forbidden.
- Real endpoint calls are forbidden.
- Real credential configuration, reads, and display are forbidden.
- `query_cloud_data_readonly` remains disabled, hidden, and non-executable.

## Authorization Template Boundary

The template must include:

- Pilot Window: name, start and end time, owner, approver, executor, rollback owner, and emergency stop owner.
- Pilot users: 5-10 users, role, department, permission scope, and approval status.
- Endpoint allowlist: `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Data boundary: last 7 days and default `maxRows=50`.
- Output boundary: draft artifacts, Final Approval, final lock, and hash-only operations ledger.
- Credential responsibility: configuration owner, custodian, approver, configuration window, and rollback requirement.

The template must not contain a real token, API key, connection string, full SQL, runtime rows, raw payload, or sensitive context.

## Blocking Ledger

Default blockers:

- `MissingExplicitExecutionRequest`
- `MissingSignedApproval`
- `MissingFrozenPilotWindow`
- `MissingExecutorOrApprover`
- `MissingRollbackOrEmergencyStopOwner`
- `MissingCredentialWindow`
- `MissingDryRunEvidence`

Unsafe boundary blocker:

- `UnsafeProductionBoundary`

## Go/No-Go

- `BlockedByMissingAuthorizationMaterials`: default state while required signed and execution materials are missing.
- `BlockedByUnsafeBoundary`: any unsafe production boundary evidence is found.
- `ReadyForUserMaterialSubmission`: the template is complete and can be handed to the user for manual filling, but execution remains forbidden.
- `ReadyForCredentialWindowPlanning`: only possible after future user-supplied signed materials clear the blocking ledger; it still does not execute a real Pilot.

## Stop Conditions

Stop immediately if implementation requires Cloud/Edge changes, a real endpoint, real credentials, Cloud write, Recipe/version access, free SQL, raw payload retention, runtime rows in reports, or enabling `query_cloud_data_readonly`.
