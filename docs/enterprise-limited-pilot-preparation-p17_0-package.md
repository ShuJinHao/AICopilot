# AICopilot Limited Pilot Preparation P17.0 Package

## Purpose

This package freezes the inputs needed for a future limited real-production readonly Pilot. It is a preparation artifact only. It does not execute the Pilot and does not configure runtime credentials.

## Entry Requirements

- P16.6 formal 5.5 Pro review result is `NoBlocker`.
- PR #48 current head has `simulation-rc` success.
- Pilot Window is approved, active, and not expired.
- Tool Approval and Final Approval are available.
- Emergency stop owner and rollback owner are assigned.
- Rows retention is runtime-only, and operations ledger remains hash-only.

If P16.6 is `ReviewPending` or `BlockedByReview`, the Go/No-Go result is blocked.

## Pilot Window Inputs

- Pilot name: limited production readonly Pilot.
- User scope: 5-10 approved pilot users.
- Endpoint allowlist: `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Default time range: last 7 days.
- Default maxRows: 50.
- Output types: Markdown, HTML, PDF, PPTX, XLSX draft artifacts and final approval artifacts.
- Required approvals: Tool Approval before query, Final Approval before final artifact state.

## Credential Handling Plan

- Runtime credentials are not stored in this package.
- Future credential configuration must be approved against a named Pilot Window.
- Credential custody, configuration operator, approver, and rollback owner must be recorded before execution.
- Reports and frontends may only display configured/not-configured status, never real token, API Key, connection string, or secret content.

## Rollback And Emergency Stop

- Emergency stop must be rehearsed before execution.
- Emergency stop must block P12 fixed-template Pilot and P13 controlled Pilot paths.
- Clearing emergency stop does not bypass gate, window, approval, or credential checks.
- Rollback must include disabling the Pilot Window, revoking runtime credential configuration, and preserving hash-only audit evidence.

## Rows Retention And Artifact Use

- Runtime rows are not persisted by operations ledger.
- Operations ledger remains hash-only.
- Draft and final artifacts may only use approved, bounded, truncated, and source-marked data.
- Reports must not expose raw payload, raw business records, full SQL, token, API Key, connection string, or sensitive context.

## Go/No-Go States

- `BlockedByP16ReviewPending`: P16.6 review is still pending.
- `BlockedByP16Review`: P16.6 review has blocker findings.
- `BlockedByAcceptanceFailure`: preparation validation failed.
- `ReadyForLimitedPilotPreparation`: P16.6 has no blocker and preparation material is complete.

`ReadyForLimitedPilotPreparation` is not execution permission. Actual execution still requires an approved Pilot Window, approval chain, rollback owner, emergency stop readiness, and approved runtime credential configuration.
