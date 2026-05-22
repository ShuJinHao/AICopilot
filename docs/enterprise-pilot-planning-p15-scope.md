# AICopilot Enterprise Pilot Planning P15 Scope Freeze

## Stage Position

P15 is a production readonly Pilot planning and authorization gate. It is not P16 real Pilot execution and not GA.

P15 prepares a reviewable authorization package from existing P12/P13/P14.2 evidence. It does not broaden production read capability.

## Allowed

- Modify only `AICopilot`.
- Add planning documents, acceptance script, report, and minimal read-only frontend evidence.
- Reuse existing P12/P13/P14.2 evidence.
- List P16 engineering blockers without implementing them in P15.

## Forbidden

- Do not modify `IIoT.CloudPlatform`.
- Do not modify `IIoT.EdgeClient`.
- Do not add Cloud endpoints.
- Do not open Cloud write.
- Do not open Recipe or Recipe version.
- Do not open free SQL.
- Do not open `query_cloud_data_readonly`.
- Do not present P15 as real Pilot execution or GA.
- Do not output token, API Key, connection string, full SQL, raw payload, rows, or sensitive context.

## Default Pilot Boundary

- Users: 5-10 internal pilot users.
- Roles: Admin, TrialManager, Approver, Operator, Viewer.
- Endpoints: `devices`, `capacity_summary`, `device_logs`, `pass_station_records`.
- Time range: latest 7 days.
- Default maxRows: 50.
- Artifacts: Markdown, HTML, PDF, PPTX, XLSX drafts and final-approved outputs.

## P16 Blockers

- Persist P12 `ProductionPilotWindow` and `ProductionPilotRun`.
- Persist P13 `ProductionControlledPilotIntent` and `ProductionControlledPilotRun`.
- Automatically backfill final artifact refs into `ProductionPilotRunLedger`.
- Define P12/P13 rows retention, masking, TTL, download, and artifact-use policy.
- Add P14 operations permission smoke.
- Add long-running and concurrency validation.

## Completion Rule

P15 can produce `ReadyForP16Planning`, but must not produce `ReadyForP16Execution` while any blocker remains open.
