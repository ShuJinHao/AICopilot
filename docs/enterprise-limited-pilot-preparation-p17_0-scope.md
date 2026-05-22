# AICopilot Limited Pilot Preparation P17.0 Scope

## Stage Position

P17.0 prepares the limited real-production readonly Pilot execution package. It is not real Pilot execution, does not configure or call a real endpoint/token, and is not GA.

P17.0 is conditional. If the latest P16.6 formal 5.5 Pro review result is `ReviewPending` or `BlockedByReview`, P17.0 material must remain blocked and planning-only.

## Boundaries

- AICopilot only.
- No `IIoT.CloudPlatform` changes.
- No `IIoT.EdgeClient` changes.
- No new Cloud endpoint.
- No Cloud write.
- No Recipe or Recipe version reads.
- No free SQL.
- `query_cloud_data_readonly` remains disabled, hidden, and non-executable.
- No real endpoint/token is configured or called by P17.0.

## Allowed Work

- Prepare a Pilot Window package for a future limited Pilot.
- Prepare a credential handling plan without storing real secrets.
- Prepare a Go/No-Go checklist for execution approval.
- Keep the current state blocked when P16.6 remains pending or blocked.
- Refresh PR wording so execution preparation is not mistaken for execution.

## Non-Goals

- P17.0 does not run a production query.
- P17.0 does not approve runtime credentials.
- P17.0 does not open `query_cloud_data_readonly`.
- P17.0 does not grant GA.

## Completion Conditions

- P17.0 scope and preparation package documents exist.
- P17.0 acceptance report exists and is sanitized.
- Report states the latest P16.6 review result and the resulting Go/No-Go decision.
- Pending or blocked review must produce a blocked Go/No-Go state.
- Report does not claim real Pilot execution, real endpoint/token use, or GA.
