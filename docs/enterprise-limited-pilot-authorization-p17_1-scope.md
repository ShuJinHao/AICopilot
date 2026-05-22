# AICopilot Limited Pilot Authorization P17.1 Scope

## Stage Position

P17.1 prepares the internal authorization package and dry-run rehearsal checklist for a future limited real-production readonly Pilot.

P17.1 is not real Pilot execution, does not configure or call a real endpoint/token, and is not GA. The external 5.5 Pro review state is recorded as evidence only and is not used as a blocker for preparing authorization material.

## Boundaries

- AICopilot only.
- No `IIoT.CloudPlatform` changes.
- No `IIoT.EdgeClient` changes.
- No new Cloud endpoint.
- No Cloud write.
- No Recipe or Recipe version reads.
- No free SQL.
- `query_cloud_data_readonly` remains disabled, hidden, and non-executable.
- No real endpoint/token is configured, stored, displayed, or called by P17.1.

## Allowed Work

- Prepare an internal Pilot Window authorization draft.
- Prepare a 5-10 user pilot roster template.
- Freeze the readonly endpoint allowlist and data boundary.
- Prepare a dry-run rehearsal checklist that uses fake/fixture inputs only.
- Prepare an execution runbook that records responsibilities and checks, without secrets.
- Refresh a sanitized acceptance report for review.

## Non-Goals

- P17.1 does not run a production query.
- P17.1 does not approve runtime credentials.
- P17.1 does not open `query_cloud_data_readonly`.
- P17.1 does not grant GA.
- P17.1 does not replace future explicit user approval for real Pilot execution.

## Completion Conditions

- P17.1 scope and authorization package documents exist.
- P17.1 acceptance report exists and is sanitized.
- Report states that external review status is evidence only.
- Dry-run checklist confirms fake/fixture-only rehearsal.
- Report does not claim real Pilot execution, real endpoint/token use, or GA.
