# AICopilot Enterprise AI Readiness Baseline Checklist

Version: 2026-05-24

## Baseline Acceptance

- [x] `M1-M6 + Batch 5-10 readiness baseline` is accepted as `AICopilot Enterprise AI Readiness Baseline v1`.
- [x] Current status is staged readiness closure only.
- [x] Current status is not real Pilot execution.
- [x] Current status is not production rollout approval.
- [x] Current status is not GA.
- [x] Follow-up productionization work is split into later PRs.

## Required Evidence

- [x] Enterprise governance baseline and checklist exist.
- [x] M2 Pilot Authorization Workflow is implemented and documented.
- [x] M2.1 hardening covers sensitive draft blocking, self-review prohibition, expiration lifecycle, and M7 material intake.
- [x] Batch 5-10 readiness covers audit timeline, sensitive content guard enhancement, M3/M4/M5 design freezes, and M7 dry-run readiness.
- [x] M7 real Pilot hard stop remains active.
- [x] M7 dry-run/readiness evidence is explicitly not execution.

## Hard Gates

- [x] `ExecutionPermission=not granted`.
- [x] `GateState=BlockedUntilExplicitM7Authorization`.
- [x] Planning/readiness/dry-run does not become execution permission.
- [x] GPT/5.5 Pro review evidence does not become M7 real Pilot authorization.
- [x] Real Pilot requires later standalone authorization.

## Forbidden Changes

- [x] No `IIoT.CloudPlatform` changes.
- [x] No `IIoT.EdgeClient` changes.
- [x] No AICopilot frontend changes.
- [x] No source code changes.
- [x] No script changes.
- [x] No appsettings changes.
- [x] No migrations.
- [x] No runtime behavior changes.
- [x] No real endpoint/token/API key/connection string configuration.
- [x] No Cloud write.
- [x] No Recipe or Recipe version access.
- [x] No free SQL.
- [x] No `query_cloud_data_readonly` enablement.
- [x] No raw payload, full SQL, raw business rows, token, API key, or connection string output.
- [x] No GA declaration.

## Validation Checklist

- [ ] `git diff --check`.
- [ ] `pwsh -ExecutionPolicy Bypass -File ./scripts/Test-TextEncoding.ps1`.
- [ ] `pwsh -ExecutionPolicy Bypass -File ./scripts/Test-AICopilotM2M9GovernanceScope.ps1`.
- [ ] `dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj --no-restore`.
- [ ] `dotnet build src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`.
- [ ] `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Suite=PilotAuthorizationWorkflowM2"`.
- [ ] `git diff --name-only` and `git ls-files --others --exclude-standard` show only the five approved baseline closure documents before commit.

## Completion Standard

This closure is complete only when the five baseline documents exist, diff scope remains limited to those five documents, all available validations pass, and any unavailable validation is recorded explicitly.
