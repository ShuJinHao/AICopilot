# AICopilot Production Pilot Execution Readiness P16.2 Package

## Purpose

P16.2 is the execution-readiness review package for a future limited production readonly Pilot. It is not real Pilot execution and not GA.

The package is based on PR #48 P16.0 hardening evidence. It must be reviewed against the latest PR head and current GitHub CI result before any limited Pilot execution plan is produced.

## Current Boundary

- AICopilot only.
- No `IIoT.CloudPlatform` changes.
- No `IIoT.EdgeClient` changes.
- No Cloud write.
- No Recipe or Recipe version reads.
- No free SQL.
- `query_cloud_data_readonly` remains disabled, hidden, and non-executable.
- No real endpoint/token is used by P16.2.

## Required Pilot Window Inputs

Before a limited production readonly Pilot can be executed, the operator must provide and approve:

- Pilot Window name.
- Start and end time.
- Owner department.
- Pilot owner.
- Data approver.
- Tool approval owner.
- Final approval owner.
- Rollback owner.
- Emergency stop owner.
- Explicit rollback and emergency-stop instructions.

## Allowed Runtime Boundary

The initial limited Pilot execution plan may only use:

- Endpoints: `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Default time range: latest 7 days.
- Default `maxRows`: 50.
- Output artifacts: Markdown, HTML, PDF, PPTX, XLSX.
- Required gates: Pilot Window, Tool Approval, Final Approval, emergency stop.

## Rows Retention And Artifact Policy

- Runtime rows are short-lived and approval-bound.
- P12/P13 persisted run stores must not persist rows.
- Operations ledger remains hash-only.
- Artifact generation may use only approved, source-marked, truncated or masked runtime rows.
- Reports, readiness, frontend status, and run ledger must not return rows or raw payload.
- Artifact downloads remain behind Artifact Workspace permission and final-approval boundaries.

## 5.5 Pro Review Checklist

The reviewer must inspect PR #48 latest head only and verify:

- P12 `ProductionPilotWindow` and `ProductionPilotRun` are persisted through `AiGatewayDbContext`.
- P13 `ProductionControlledPilotIntent` and `ProductionControlledPilotRun` are persisted through `AiGatewayDbContext`.
- P12/P13 persisted run stores do not save rows, raw payload, full SQL, token, API Key, or connection string.
- Final artifact refs are automatically backfilled into `ProductionPilotRunLedger` from finalization.
- Missing ledger cases produce controlled warning/audit instead of fake evidence.
- Emergency stop remains authoritative over P12/P13 execution.
- `query_cloud_data_readonly` remains disabled, hidden, and non-executable.
- GitHub `simulation-rc` is success for the reviewed head.

## Go/No-Go Rules

- `BlockedByReview`: 5.5 Pro reports any Blocker or GitHub CI is not success.
- `ReadyForPilotExecutionPlanning`: GitHub CI is success and this package is complete, but 5.5 Pro has not yet returned a no-Blocker conclusion.
- `ReadyForLimitedPilotExecution`: GitHub CI is success, 5.5 Pro has no Blocker, Pilot Window inputs are approved, and runtime credentials are provided through approved configuration only.

P16.2 defaults to `ReadyForPilotExecutionPlanning` until 5.5 Pro review is complete.

## Explicit Non-Goals

- P16.2 does not run a real Pilot.
- P16.2 does not configure or test a real endpoint/token.
- P16.2 does not enable production free-goal expansion beyond the existing P12/P13 gates.
- P16.2 does not open GA.
