# AICopilot Enterprise Pilot Planning P15 Package

## Summary

P15 is a production readonly Pilot planning and authorization gate. It does not start real Pilot execution, does not broaden production reads, and does not enable GA.

The package exists to prepare a reviewable P16 entry decision based on P14.2 evidence.

## Pilot Boundary

- Users: 5-10 internal pilot users.
- Roles: Admin, TrialManager, Approver, Operator, Viewer.
- Allowed endpoints: `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Time range: latest 7 days.
- Default max rows: 50.
- Artifacts: Markdown, HTML, PDF, PPTX, XLSX drafts and final-approved outputs.
- Required approvals: Tool Approval before production readonly query; Final Approval before final artifact.

## Explicitly Forbidden

- Cloud write.
- Recipe and Recipe version.
- Arbitrary Cloud endpoint.
- Free SQL.
- Unapproved final output.
- Unauthorized production Pilot user.
- `query_cloud_data_readonly`.
- Token, API Key, connection string, full SQL, raw payload, rows, or sensitive context in reports/UI.

## Authorization Matrix

| Role | Read status | Run P12/P13 Pilot | Approve tool | Approve final | Manage incident | Emergency stop | P15 evaluation |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Admin | Yes | Yes | Yes | Yes | Yes | Yes | Yes |
| TrialManager | Yes | Yes | No | No | Yes | Yes | Yes |
| Approver | Yes | No | Yes | Yes | No | No | No |
| Operator | Yes | No | No | No | No | No | No |
| Viewer | Yes | No | No | No | No | No | No |

## Data Retention Policy

- Operations ledger is hash-only.
- P12/P13 rows are not retained in operations ledger.
- P12/P13 rows require a P16 retention decision before real Pilot execution.
- Draft artifacts may include approved summaries/tables under the final-output approval boundary.
- Final artifacts are immutable after approval.
- Reports use endpoint, row count, truncation status, query/result hash, approval status, and artifact references.

## P16 Blockers

P16 implementation is blocked until these are closed:

1. Persist P12 `ProductionPilotWindow` and `ProductionPilotRun`.
2. Persist P13 `ProductionControlledPilotIntent` and `ProductionControlledPilotRun`.
3. Automatically backfill final artifact refs into `ProductionPilotRunLedger`.
4. Define P12/P13 rows retention, masking, TTL, download, and artifact-use policy.
5. Add operations permission smoke for ordinary-user rejection and authorized-manager success.
6. Add long-running and concurrency validation for multi-user Pilot operations.

## Go / No-Go Rule

P15 can only conclude `ReadyForP16Planning` when the planning package is complete and inherited P14.2 checks pass.

P15 must not conclude `ReadyForP16Execution` while any P16 blocker remains open.
