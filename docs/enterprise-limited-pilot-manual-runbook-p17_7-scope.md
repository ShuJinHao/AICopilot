# AICopilot P17.7 Scope Freeze: Manual Execution Runbook And Offline Preflight

## Purpose

P17.7 freezes the manual execution runbook and offline preflight package for a future limited production readonly Pilot.

This stage does not execute a real Pilot, does not configure or read real endpoint credentials, does not call a real endpoint, and is not GA.

## Boundary

- Modify only `AICopilot` documentation, reports, acceptance scripts, and readonly diagnostics.
- Do not modify `IIoT.CloudPlatform` or `IIoT.EdgeClient`.
- Do not add Cloud endpoints.
- Do not add Cloud write capability.
- Do not enable Recipe or Recipe version access.
- Do not add free SQL.
- Keep `query_cloud_data_readonly` disabled, hidden, and non-executable.

## Go/No-Go States

- `BlockedNoSignedApproval`: default state; no signed approval has been received and execution is not allowed.
- `BlockedRunbookIncomplete`: the runbook is missing Pilot Window, roles, approval chain, rollback, or emergency stop requirements.
- `BlockedCredentialPreflightIncomplete`: credential configuration responsibility or configuration window is incomplete.
- `ReadyForExplicitManualExecutionRequest`: all materials are complete, but a future user message must still explicitly request the manual execution step.

## Manual Execution Runbook Boundary

- Pilot Window must include name, start time, end time, owner, approver, executor, rollback owner, and emergency stop owner.
- Pilot users remain limited to 5-10 approved users.
- Endpoint allowlist remains `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Data boundary remains last 7 days with default `maxRows=50`.
- Tool Approval is required before any future readonly query.
- Final Approval is required before any future final artifact.
- Operations ledger remains hash-only.

## Offline Preflight Boundary

- Offline preflight may check material completeness, status fields, approval placeholders, rollback steps, and emergency stop online verification requirements.
- Offline preflight must not write, read, or display a real token, API key, connection string, full SQL, raw payload, or raw business record output.
- Offline preflight must not call a real endpoint.
- Offline preflight must not generate real production artifacts.

