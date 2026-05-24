# AICopilot Limited Pilot Manual Execution Runbook P17.7 Package

## Purpose

This package freezes the manual execution runbook and offline preflight checklist for a future limited production readonly Pilot. It does not execute the Pilot and does not configure runtime credentials.

## Current State

- Default Go/No-Go: `BlockedNoSignedApproval`.
- Current signed approval state: no signed approval received.
- Current execution permission: not granted.
- Current credential status: responsibility placeholder only.
- Current endpoint status: allowlist documented only.

## Manual Execution Runbook

| Field | Required Value | Current Placeholder |
| --- | --- | --- |
| Signed approval received | true before future execution | false |
| Pilot Window approved | true before future execution | false |
| Pilot Window name | approved limited production readonly Pilot | to be signed |
| Pilot Window start and end | approved bounded window | to be signed |
| Owner | named human owner | to be signed |
| Approver | named human approver | to be signed |
| Executor | named human executor | to be signed |
| Rollback owner | named human rollback owner | to be signed |
| Emergency stop owner | named human emergency stop owner | to be signed |
| Pilot users | 5-10 approved users | to be signed |
| Tool Approval | required | required |
| Final Approval | required | required |
| Operations ledger | hash-only | hash-only |

## Fixed Pilot Boundary

- Endpoint allowlist: `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Default time range: last 7 days.
- Default maxRows=50.
- Output boundary: draft artifacts only until Final Approval; final artifacts are locked after approval.
- Forbidden actions: Cloud write, Recipe/version, free SQL, unapproved final, unauthorized users, `query_cloud_data_readonly`.

## Offline Preflight Package

- Material completeness check: required.
- Approval placeholder check: required.
- Pilot Window status check: required before any future execution.
- Emergency stop online verification: required before any future execution.
- Rollback checklist: disable the future execution window and revoke the future runtime configuration path.
- Credential configuration owner: to be signed.
- Credential custodian: to be signed.
- Credential approver: to be signed.
- Credential configuration window: to be signed.
- Real credential read/write/display: forbidden in P17.7.
- Real endpoint call: forbidden in P17.7.

## Go/No-Go Rules

- `BlockedNoSignedApproval`: default; keep the system non-executable.
- `BlockedRunbookIncomplete`: keep the system non-executable and request corrected runbook material.
- `BlockedCredentialPreflightIncomplete`: keep the system non-executable and request corrected credential preflight material.
- `ReadyForExplicitManualExecutionRequest`: only allows waiting for a later explicit manual execution request; it does not call a real endpoint.

## Future Execution Boundary

P17.7 success does not grant real Pilot execution. Real execution still requires a separate user instruction, signed approval, approved Pilot Window, approved credential configuration, executor confirmation, rollback window, emergency stop online verification, and post-execution audit archival.

