# AICopilot P18.1 Scope Freeze: Fillable Authorization Template And Offline Submission Validation

## Purpose

P18.1 turns the P18.0 authorization intake requirements into a fillable limited Pilot authorization template and an offline submission validation package.

P18.1 does not execute a real Pilot, does not configure credentials, does not call a real endpoint, does not generate real production artifacts, and is not GA.

## Allowed Scope

- Modify only AICopilot documentation, reports, acceptance scripts, and readonly diagnostic material.
- Preserve P18.0 authorization intake evidence.
- Provide a fillable template for future user-submitted materials.
- Provide safe, invalid, and unsafe sample material shapes for offline validation.
- Validate material completeness and unsafe content markers without touching real credentials.

## Frozen Areas

- IIoT.CloudPlatform is frozen.
- IIoT.EdgeClient is frozen.
- Cloud write is forbidden.
- Recipe and Recipe version are forbidden.
- Free SQL and arbitrary endpoint payloads are forbidden.
- Real endpoint calls are forbidden.
- Real credential input, reads, writes, validation, and display are forbidden.
- `query_cloud_data_readonly` remains disabled, hidden, and non-executable.

## Fillable Template Boundary

The fillable template must require:

- Pilot Window: name, start and end time, owner, approver, executor, rollback owner, and emergency stop owner.
- Pilot users: 5-10 users, role, department, permission scope, and approval status.
- Endpoint allowlist: `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Data boundary: last 7 days and default `maxRows=50`.
- Output boundary: draft artifacts, Final Approval, final lock, and hash-only operations ledger.
- Credential responsibility: configuration owner, custodian, approver, configuration window, rollback requirement, and emergency stop online verification requirement.

The template must not include a real token, API key, connection string, full SQL, runtime rows, raw payload, or sensitive context.

## Offline Validation Rules

- `BlockedNoSubmittedAuthorizationMaterials`: no material has been submitted.
- `BlockedInvalidAuthorizationMaterials`: required fields are missing, pilot users are outside 5-10, endpoint is outside the allowlist, time range exceeds last 7 days, or `maxRows` exceeds 50.
- `BlockedUnsafeCredentialMaterial`: real credential material, raw payload, runtime rows, full SQL, or sensitive context markers are present.
- `ReadyForCredentialWindowPlanning`: material is complete and safe enough for later credential-window planning; it still does not configure credentials or execute a real Pilot.

## Sample Set

- Safe sample: complete material with placeholders and no real credential material.
- Invalid sample: missing approver, too few pilot users, endpoint overflow, or `maxRows` above 50.
- Unsafe sample: marks presence of credential material, raw payload, runtime rows, or full SQL without containing real values.

## Stop Conditions

Stop immediately if implementation requires Cloud/Edge changes, a real endpoint, real credentials, Cloud write, Recipe/version access, free SQL, raw payload retention, runtime rows in reports, or enabling `query_cloud_data_readonly`.
