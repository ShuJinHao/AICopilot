# AICopilot Production Pilot Execution Plan P16.3 Package

## Purpose

P16.3 freezes the limited production readonly Pilot execution plan and records the review state for 5.5 Pro. It is planning and review material only. It does not execute a real Pilot, does not configure a real endpoint or token, and is not GA.

## Current Boundary

- AICopilot only.
- No `IIoT.CloudPlatform` changes.
- No `IIoT.EdgeClient` changes.
- No new Cloud endpoint.
- No Cloud write.
- No Recipe or Recipe version reads.
- No free SQL.
- `query_cloud_data_readonly` remains disabled, hidden, and non-executable.
- No real endpoint/token is used by P16.3.

## Pilot Window Inputs To Freeze

Before a limited production readonly Pilot can be executed, the approved runtime plan must provide:

- Pilot Window name.
- Start time and end time.
- Owner department.
- Pilot owner.
- Data approver.
- Tool approval owner.
- Final approval owner.
- Rollback owner.
- Emergency stop owner.
- Rollback instruction.
- Emergency stop instruction.
- Approved runtime credential configuration reference.

## Execution Boundary

The first limited production readonly Pilot execution may only use:

- Endpoint allowlist: `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Default time range: latest 7 days.
- Default `maxRows`: 50.
- Output artifacts: Markdown, HTML, PDF, PPTX, XLSX.
- Required gates: Pilot Window, Tool Approval, Final Approval, emergency stop.
- Evidence: source mode, boundary, endpoint code, approval state, duration, count, truncation marker, query hash, result hash, final artifact refs.

## Required Runtime Flow

The execution plan is frozen to this sequence:

1. Pilot Window becomes active.
2. User selects an approved P12 fixed template or approved P13 controlled target.
3. Backend validates gate, endpoint allowlist, time range, `maxRows`, and emergency stop.
4. Tool Approval is granted.
5. Readonly query runs through the approved production Pilot tool.
6. Draft artifact is generated with source labels and hashes.
7. Final Approval is granted.
8. Final artifact is locked.
9. Production operations ledger receives hash-only evidence and final artifact refs.

Any missing gate, missing approval, active emergency stop, disallowed endpoint, over-range request, Recipe/version request, Cloud write intent, free SQL, or unsafe protected tool state must block execution.

## Record Retention And Artifact Policy

- Runtime business records are short-lived and approval-bound.
- P12/P13 persisted run stores must not persist raw business records.
- Production operations ledger remains hash-only.
- Artifact generation may use only approved, source-marked, truncated or masked runtime records.
- Reports, readiness material, frontend status, and run ledger must not return raw payload or raw business records.
- Artifact downloads remain behind Artifact Workspace permission and final approval.

## 5.5 Pro Review Ledger

Current review status: `ReviewPending`.

Allowed review outcomes:

- `BlockedByReview`: 5.5 Pro reports any Blocker, or GitHub current head CI is not success.
- `ReadyForLimitedPilotExecutionPlanning`: CI is success and this execution plan package is complete, but review is still pending or permits planning only.
- `ReadyForLimitedPilotExecution`: CI is success, 5.5 Pro reports no Blocker, Pilot Window inputs are approved, and runtime credentials are configured only through approved configuration.

P16.3 defaults to `ReadyForLimitedPilotExecutionPlanning`. It must not claim `ReadyForLimitedPilotExecution` while 5.5 Pro is pending.

## Review Checklist

5.5 Pro should review PR #48 latest head only and verify:

- P16.0/P16.2/P16.3 only modify `AICopilot`.
- P12/P13 window, run, and intent stores are persisted through `AiGatewayDbContext`.
- P12/P13 persisted run stores do not persist raw business records, raw payload, full SQL, token, API Key, or connection string.
- Final artifact refs are automatically backfilled into `ProductionPilotRunLedger`.
- Emergency stop remains authoritative over P12/P13 execution.
- `query_cloud_data_readonly` remains disabled, hidden, and non-executable.
- P16.3 report states planning/review only and does not claim real Pilot execution.
- GitHub `simulation-rc` is success for the reviewed head.

## Explicit Non-Goals

- P16.3 does not execute a real Pilot.
- P16.3 does not configure or test a real endpoint/token.
- P16.3 does not enable GA.
- P16.3 does not enable Cloud write, Recipe/version, free SQL, or `query_cloud_data_readonly`.

