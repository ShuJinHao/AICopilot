# AICopilot Limited Pilot Dry-Run P17.2 Scope

## Stage Position

P17.2 turns the P17.1 internal authorization package into a repeatable fake/fixture dry-run rehearsal. It validates the future limited Pilot control chain without calling a real endpoint or configuring a real credential.

P17.2 is not real Pilot execution, does not configure or call a real endpoint/token, and is not GA. External 5.5 Pro review state is recorded as evidence only.

## Boundaries

- AICopilot only.
- No `IIoT.CloudPlatform` changes.
- No `IIoT.EdgeClient` changes.
- No new Cloud endpoint.
- No Cloud write.
- No Recipe or Recipe version reads.
- No free SQL.
- `query_cloud_data_readonly` remains disabled, hidden, and non-executable.
- No real endpoint/token is configured, stored, displayed, or called by P17.2.

## Allowed Work

- Add a repeatable dry-run acceptance runner based on fake/fixture inputs.
- Cover fixed-template and controlled-goal Pilot paths.
- Verify the allowlist endpoints: `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Verify the default data boundary: last 7 days and maxRows=50.
- Verify Tool Approval, Final Approval, emergency stop, rollback, and hash-only ledger evidence.
- Generate a sanitized dry-run evidence report.

## Non-Goals

- P17.2 does not run a production query.
- P17.2 does not approve runtime credentials.
- P17.2 does not open `query_cloud_data_readonly`.
- P17.2 does not grant GA.
- P17.2 does not replace future explicit user approval for real Pilot execution.

## Completion Conditions

- P17.2 scope and evidence package documents exist.
- P17.2 dry-run acceptance script exists and passes.
- Dry-run evidence covers fixed-template and controlled-goal paths for all four allowlist endpoints.
- Emergency stop and rollback rehearsal evidence is present.
- Report does not claim real Pilot execution, real endpoint/token use, or GA.
