# AICopilot P18.2 Scope Freeze: Offline Authorization Submission And Machine Validation

## Purpose

P18.2 turns the P18.1 fillable authorization template into a fixed offline submission format and machine-validation gate for future user-filled authorization materials.

P18.2 does not execute a real Pilot, does not configure credentials, does not call a real endpoint, does not generate real production artifacts, and is not GA.

## Allowed Scope

- Modify only AICopilot documentation, reports, acceptance scripts, and readonly diagnostic material.
- Preserve P18.1 fillable template evidence.
- Define the `LimitedPilotAuthorizationSubmission` offline material format.
- Provide safe, invalid, and unsafe submission package shapes for machine validation.
- Validate submitted material completeness and unsafe content markers without touching real credentials.

## Frozen Areas

- IIoT.CloudPlatform is frozen.
- IIoT.EdgeClient is frozen.
- Cloud write is forbidden.
- Recipe and Recipe version are forbidden.
- Free SQL and arbitrary endpoint payloads are forbidden.
- Real endpoint calls are forbidden.
- Real credential input, reads, writes, validation, and display are forbidden.
- `query_cloud_data_readonly` remains disabled, hidden, and non-executable.

## Submission Format Boundary

`LimitedPilotAuthorizationSubmission` must include:

- Pilot Window: name, start and end time, owner, approver, executor, rollback owner, and emergency stop owner.
- Pilot users: 5-10 users, role, department, permission scope, and approval status.
- Endpoint allowlist: `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Data boundary: last 7 days and default `maxRows=50`.
- Output boundary: draft artifacts, Final Approval, final lock, and hash-only operations ledger.
- Credential responsibility: configuration owner, custodian, approver, configuration window, rollback requirement, and emergency stop online verification requirement.

The submission package must not include a real token, API key, connection string, full SQL, runtime rows, raw payload, or sensitive context.

## Machine Validation Rules

- `BlockedNoSubmittedAuthorizationMaterials`: no material has been submitted.
- `BlockedInvalidAuthorizationMaterials`: submission format is invalid, required fields are missing, pilot users are outside 5-10, endpoint is outside the allowlist, time range exceeds last 7 days, or `maxRows` exceeds 50.
- `BlockedUnsafeCredentialMaterial`: real credential material, raw payload, runtime rows, full SQL, or sensitive context markers are present.
- `ReadyForCredentialWindowPlanning`: material is complete and safe enough for later credential-window planning; it still does not configure credentials or execute a real Pilot.

## Sample Set

- Safe submission package: complete placeholders and no real credential material.
- Invalid submission package: missing approver, too few pilot users, forbidden endpoint, or `maxRows` above 50.
- Unsafe submission package: marks credential material, raw payload, runtime rows, or full SQL risks without containing real values.

## Stop Conditions

Stop immediately if implementation requires Cloud/Edge changes, a real endpoint, real credentials, Cloud write, Recipe/version access, free SQL, raw payload retention, runtime rows in reports, or enabling `query_cloud_data_readonly`.
