# AICopilot P17.8 Scope Freeze: Explicit Manual Execution Request Intake

## Purpose

P17.8 receives and validates a future explicit manual execution request for the limited production readonly Pilot. It freezes the final pre-execution gate before any credential configuration window can be prepared.

P17.8 does not execute a real Pilot, does not connect to a real endpoint, does not configure or read real credentials, does not generate real production artifacts, and is not GA.

## Allowed Scope

- Modify only AICopilot documentation, reports, acceptance scripts, and readonly diagnostic material.
- Preserve P17.7 manual runbook and offline preflight evidence.
- Record an explicit manual execution request package without executing it.
- Validate that a future request is bound to a frozen Pilot Window, signed approval evidence, executor, approval chain, rollback owner, emergency stop owner, fixed endpoint allowlist, and data boundary.
- Keep all credential references as responsibility and window placeholders only.

## Frozen Areas

- IIoT.CloudPlatform is frozen.
- IIoT.EdgeClient is frozen.
- Cloud write is forbidden.
- Recipe and Recipe version are forbidden.
- Free SQL and arbitrary endpoint payloads are forbidden.
- Real endpoint calls are forbidden.
- Real credential configuration, reads, and display are forbidden.
- `query_cloud_data_readonly` remains disabled, hidden, and non-executable.

## Fixed Pilot Boundary

- Endpoint allowlist: `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Data boundary: last 7 days.
- Default `maxRows=50`.
- Tool Approval is required.
- Final Approval is required.
- Operations ledger remains hash-only.
- Runtime rows and raw payload are not retained by operations reports.

## Final Pre-Execution Go/No-Go

- `BlockedNoExplicitExecutionRequest`: default state; no explicit user execution request has been received.
- `BlockedSignedApprovalMissing`: signed approval or frozen Pilot Window evidence is missing.
- `BlockedExecutionRequestIncomplete`: executor, approval chain, rollback owner, emergency stop owner, endpoint allowlist, or data boundary is incomplete.
- `BlockedCredentialWindowMissing`: credential configuration window or custody responsibility is missing.
- `ReadyForCredentialConfigurationWindow`: all materials are complete, but this only permits a later credential configuration window preparation. It does not permit execution.

## Stop Conditions

Stop immediately if implementation requires Cloud/Edge changes, a real endpoint, real credentials, Cloud write, Recipe/version access, free SQL, raw payload retention, runtime rows in reports, or enabling `query_cloud_data_readonly`.
