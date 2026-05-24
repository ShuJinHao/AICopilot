# AICopilot P17.6 Scope Freeze: Signed Approval Intake And Manual Execution Step Freeze

## Purpose

P17.6 records whether a signed execution approval has been received and freezes the manual execution-step checklist for a future limited production readonly Pilot.

This stage does not execute a real Pilot, does not configure or read real endpoint credentials, and is not GA.

## Boundary

- Modify only `AICopilot` documentation, reports, acceptance scripts, and readonly diagnostics.
- Do not modify `IIoT.CloudPlatform` or `IIoT.EdgeClient`.
- Do not add Cloud endpoints.
- Do not add Cloud write capability.
- Do not enable Recipe or Recipe version access.
- Do not add free SQL.
- Keep `query_cloud_data_readonly` disabled, hidden, and non-executable.

## Intake Decisions

- `NoSignedApprovalReceived`: default state; no human-signed approval has been received and execution is not allowed.
- `SignedApprovalIncomplete`: signed material is missing Pilot Window, approver, executor, rollback owner, emergency stop owner, endpoint allowlist, or data boundary.
- `CredentialWindowIncomplete`: credential configuration responsibility, custody, configuration window, or rollback requirement is incomplete.
- `ReadyForManualExecutionStepPlanning`: signed material is complete and may be handed to a later manual execution-step plan; this is not execution.

## Manual Execution Checklist Boundary

- Pilot Window must include name, start time, end time, owner, approver, executor, rollback owner, and emergency stop owner.
- Pilot users remain limited to 5-10 approved users.
- Endpoint allowlist remains `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Data boundary remains last 7 days with default `maxRows=50`.
- Credential material may record responsibility and configuration window only.
- Real token, API key, connection string, full SQL, raw payload, and raw business record output are forbidden.

