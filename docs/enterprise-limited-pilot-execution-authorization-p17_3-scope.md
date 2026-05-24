# AICopilot Limited Pilot Execution Authorization P17.3 Scope

## Stage Position

P17.3 turns the P17.2 fake/fixture dry-run evidence into a human execution authorization request package. It prepares the material needed for a future explicit approval decision.

P17.3 is not real Pilot execution, does not configure or call a real endpoint/token, and is not GA. External 5.5 Pro review state is recorded as evidence only. The primary blocker is whether the user has explicitly approved real Pilot execution.

## Boundaries

- AICopilot only.
- No `IIoT.CloudPlatform` changes.
- No `IIoT.EdgeClient` changes.
- No new Cloud endpoint.
- No Cloud write.
- No Recipe or Recipe version reads.
- No free SQL.
- `query_cloud_data_readonly` remains disabled, hidden, and non-executable.
- No real endpoint/token is configured, stored, displayed, or called by P17.3.

## Allowed Work

- Prepare a Pilot Execution Authorization Request.
- Prepare a credential readiness preflight without real credentials.
- Verify P17.2 dry-run evidence is complete.
- Produce a Go/No-Go report that defaults to `MissingAuthorization` unless explicit user execution approval is recorded.
- Generate a sanitized authorization report.

## Non-Goals

- P17.3 does not run a production query.
- P17.3 does not approve runtime credentials.
- P17.3 does not open `query_cloud_data_readonly`.
- P17.3 does not grant GA.
- P17.3 does not replace future explicit user approval for real Pilot execution.

## Completion Conditions

- P17.3 scope and authorization package documents exist.
- P17.3 acceptance script exists and passes.
- Report proves P17.2 dry-run inheritance.
- Report defaults to `MissingAuthorization` when no explicit execution approval is supplied.
- Report does not claim real Pilot execution, real endpoint/token use, or GA.
