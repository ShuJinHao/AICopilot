# AICopilot P18.0 Scope Freeze: Authorization Intake And Credential Window Preparation

## Purpose

P18.0 establishes the intake gate for user-submitted limited Pilot authorization materials and the preparation package for a future credential configuration window.

P18.0 does not execute a real Pilot, does not configure credentials, does not call a real endpoint, does not generate real production artifacts, and is not GA.

## Allowed Scope

- Modify only AICopilot documentation, reports, acceptance scripts, and readonly diagnostic material.
- Preserve P17.9 authorization template and blocking ledger evidence.
- Validate the shape and safety of user-submitted authorization materials.
- Prepare credential-window responsibility records without touching real credentials.
- Record Go/No-Go status for later planning only.

## Frozen Areas

- IIoT.CloudPlatform is frozen.
- IIoT.EdgeClient is frozen.
- Cloud write is forbidden.
- Recipe and Recipe version are forbidden.
- Free SQL and arbitrary endpoint payloads are forbidden.
- Real endpoint calls are forbidden.
- Real token, API key, and connection string input, reads, writes, validation, and display are forbidden.
- `query_cloud_data_readonly` remains disabled, hidden, and non-executable.

## Authorization Intake Boundary

Submitted materials must include:

- Pilot Window: name, start and end time, owner, approver, executor, rollback owner, and emergency stop owner.
- Pilot users: 5-10 users, role, department, permission scope, and approval status.
- Endpoint allowlist: `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Data boundary: last 7 days and default `maxRows=50`.
- Output boundary: draft artifacts, Final Approval, final lock, and hash-only operations ledger.
- Credential responsibility: configuration owner, custodian, approver, configuration window, rollback requirement, and emergency stop online verification requirement.

Submitted materials must not contain a real token, API key, connection string, full SQL, runtime rows, raw payload, or sensitive context.

## Credential Window Preparation

P18.0 may prepare a credential-window planning record with responsibility and approval placeholders only.

It may not write, read, validate, test, or display real credentials, and it may not call any real endpoint.

## Go/No-Go

- `BlockedNoSubmittedAuthorizationMaterials`: default state while user-filled and signed materials have not been received.
- `BlockedInvalidAuthorizationMaterials`: required fields are missing, endpoint is outside the allowlist, pilot user count is outside 5-10, time range exceeds last 7 days, or `maxRows` exceeds 50.
- `BlockedUnsafeCredentialMaterial`: real credentials, raw payload, runtime rows, full SQL, or sensitive context are found.
- `ReadyForCredentialWindowPlanning`: materials are complete and safe enough to plan a future credential window; it still does not configure credentials or execute a real Pilot.

## Stop Conditions

Stop immediately if implementation requires Cloud/Edge changes, a real endpoint, real credentials, Cloud write, Recipe/version access, free SQL, raw payload retention, runtime rows in reports, or enabling `query_cloud_data_readonly`.
