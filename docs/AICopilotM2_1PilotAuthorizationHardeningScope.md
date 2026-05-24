# AICopilot M2.1 Pilot Authorization Hardening Scope

## Scope

This PR hardens the existing M2 Pilot Authorization Workflow before any M7 real Pilot activity.

- Project: AICopilot only.
- Task type: backend/API, persistence, permission, audit, worker, tests, and governance documentation.
- Batch coverage: Batch 0 through Batch 4 only.
- Frozen areas: IIoT.CloudPlatform, IIoT.EdgeClient, `src/vues/**`, appsettings, real endpoint/token configuration, real Pilot execution, Cloud write, Recipe/version, free SQL, and `query_cloud_data_readonly` enablement.
- M7 state: hard stop remains in effect. `ExecutionPermission=not granted`.
- Gate after planning approval: `GateState=BlockedUntilExplicitM7Authorization`.

## Implemented Hardening

- Draft create/update now runs `PilotAuthorizationSensitiveContentGuard` before persistence.
- Unsafe draft content is rejected before `Add` or `Update`, and audit uses `PilotAuthorization.UnsafeDraftRejected` without storing user input text.
- Decision text for approve, reject, revoke, and expire is checked by the same guard.
- Requester self-review is blocked for credential-window planning, limited-Pilot planning, reject, revoke, and manual expire.
- Self-review returns problem code `pilot_authorization_self_review_forbidden` and leaves submission state unchanged.
- `PilotAuthorization.Expire` is a separate permission.
- `POST /api/aigateway/pilot-authorization/submissions/{id}/expire` expires stale authorization packages without granting execution.
- `PilotAuthorizationExpiryWorker` runs only in AICopilot.DataWorker and expires due non-terminal submissions.
- M7 authorization material intake is stored on `PilotAuthorizationSubmission` as an owned value object.
- `ExpiresAt` is required for new create/update requests; existing records without it remain blocked until materials are updated.

## M7 Intake Fields

The M2.1 intake captures structured authorization metadata only. It does not store real credentials or real endpoint values.

- `BusinessScope`
- `Department`
- `PilotOwner`
- `ExecutionWindowStart`
- `ExecutionWindowEnd`
- `RollbackWindowStart`
- `RollbackWindowEnd`
- `CredentialOwner`
- `SecretStorageMode`
- `SecretReferenceNameHash`
- `PostRunAuditArchiveFormat`
- `SignedApprovalRef`
- `ExpiresAt`

The existing endpoint and owner fields remain in force:

- `EndpointCodes`
- `MaxRows`
- `TimeRangeDays`
- `DataOwner`
- `ToolOwner`
- `FinalOwner`
- `RollbackOwner`
- `EmergencyOwner`

## Forbidden In This PR

- No real Pilot execution.
- No real endpoint URL, token, API key, connection string, password, private key, JWT, DB URL, raw payload, raw rows, raw business rows, full SQL, or free SQL may be accepted into Pilot authorization stored text.
- No Cloud write.
- No Recipe/version scope.
- No query_cloud_data_readonly enablement.
- No granted execution permission.
- No GA release claim.

## Validation Record

Local validation completed on this branch:

- `git diff --check`: passed.
- `dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet build src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-build --filter "Suite=PilotAuthorizationWorkflowM2"`: passed, 25 tests.

PowerShell validation not run locally because this macOS environment does not have `pwsh` or `powershell` on PATH:

- `pwsh -ExecutionPolicy Bypass -File ./scripts/Test-TextEncoding.ps1`
- `pwsh -ExecutionPolicy Bypass -File ./scripts/Test-AICopilotM2M9GovernanceScope.ps1`
- `pwsh -ExecutionPolicy Bypass -File ./scripts/Test-AICopilotM2_1PilotAuthorizationHardeningScope.ps1`

GitHub Actions or Windows validation must cover the PowerShell checks.

## Next Gate

Passing this PR does not authorize M7. M7 requires a separate explicit authorization with real Pilot scope, formal endpoint/token handling, rollback responsibility, and post-run audit ownership.
