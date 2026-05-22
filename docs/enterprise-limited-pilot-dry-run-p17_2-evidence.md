# AICopilot Limited Pilot Dry-Run P17.2 Evidence Package

## Purpose

This package defines the evidence produced by the P17.2 fake/fixture dry-run rehearsal. It is a diagnostic package only. It does not execute the Pilot and does not configure runtime credentials.

## Dry-Run Runner Inputs

- Fixture source: fake production-like contract.
- Credential source: none.
- Endpoint allowlist: `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Default time range: last 7 days.
- Default maxRows=50.
- Pilot paths:
  - Fixed-template path.
  - Controlled-goal path.

## Evidence Contract

Each dry-run evidence item records only:

- path type.
- endpoint code.
- source mode.
- boundary.
- approval status.
- duration.
- row count.
- truncated state.
- query hash.
- result hash.
- artifact refs.
- emergency stop evidence.
- rollback evidence.

The evidence package does not store or return rows, raw payload, full SQL, token, API Key, connection string, or sensitive context.

## Required Positive Coverage

- Fixed-template path covers `devices`.
- Fixed-template path covers `capacity_summary`.
- Fixed-template path covers `device_logs`.
- Fixed-template path covers `pass_station_records`.
- Controlled-goal path covers `devices`.
- Controlled-goal path covers `capacity_summary`.
- Controlled-goal path covers `device_logs`.
- Controlled-goal path covers `pass_station_records`.
- Tool Approval is present before the fake readonly query.
- Final Approval is present before final evidence.
- Hash-only ledger evidence is present.

## Required Refusal Coverage

- Recipe endpoint is refused.
- Recipe version endpoint is refused.
- Cloud write path is refused.
- Unknown endpoint is refused.
- Over maxRows request is refused.
- Over time-range request is refused.

## Emergency Stop Rehearsal

- When emergency stop is active, fixed-template dry-run execution is refused.
- When emergency stop is active, controlled-goal dry-run execution is refused.
- After emergency stop is cleared, gate, Pilot Window, Tool Approval, and Final Approval are still required.
- Clearing emergency stop does not automatically open production execution.

## Rollback Rehearsal

- Rollback records disabled Pilot Window evidence.
- Rollback records credential revocation plan evidence.
- Rollback records hash-only ledger preservation evidence.
- Rollback does not touch a real credential.

## Future Execution Boundary

P17.2 dry-run success does not grant real Pilot execution. Real execution still requires a future explicit approval naming Pilot Window, approval chain, rollback strategy, credential configuration plan, execution owner, and emergency stop owner.
