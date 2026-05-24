# AICopilot M2.2 Authorization Observability and Readiness Scope

## Scope

This PR covers Batch 5 through Batch 10 only:

- Batch 5: Pilot Authorization audit timeline query/API/tests.
- Batch 6: sensitive pattern enhancement and guard regression tests.
- Batch 7: M3 model/API pool productionization design freeze.
- Batch 8: M4 RAG governance completion design freeze.
- Batch 9: M5 enterprise data source permission design freeze.
- Batch 10: M7 dry-run readiness package.

## Frozen Areas

- No IIoT.CloudPlatform changes.
- No IIoT.EdgeClient changes.
- No AICopilot frontend changes under `src/vues/**`.
- No appsettings changes.
- No real endpoint/token/connection string configuration.
- No real Pilot execution.
- No Cloud write, Recipe/version, free SQL, or `query_cloud_data_readonly` enablement.
- No GA declaration.

## Hard Gate

- `ExecutionPermission=not granted`.
- `GateState=BlockedUntilExplicitM7Authorization`.
- Planning/readiness is not execution.
- M7 remains hard-stopped until explicit standalone authorization supplies real Pilot scope, endpoint/token handling, credential owner, execution window, rollback owner, and emergency owner.

## Implementation Notes

- `GET /api/aigateway/pilot-authorization/submissions/{id}/audit-timeline` returns sanitized state-change timeline items only.
- Audit timeline requires `PilotAuthorization.Audit`.
- Timeline metadata is restricted to safe summary keys: `pilotAuthorizationStatus`, `endpointCount`, `maxRows`, `timeRangeDays`, `ownerCount`, and `machineValidationStatus`.
- Sensitive draft and decision guards block token/API key/header/provider-key/JWT/private-key/database-url/raw-payload/raw-row/full-SQL/free-SQL/Chinese sensitive wording before persistence.

## Validation

- `git diff --check`
- `dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj --no-restore`
- `dotnet build src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Suite=PilotAuthorizationWorkflowM2"`
- `pwsh -ExecutionPolicy Bypass -File ./scripts/Test-AICopilotM2_2AuthorizationObservabilityReadinessScope.ps1`
