# AICopilot M3.1 Model/API Pool Runtime Productionization

Version: 2026-05-24

## Scope

M3.1 hardens the existing AICopilot model/API pool runtime. It does not introduce a new gateway and does not change Cloud or Edge.

This stage touches backend/runtime only:

- `AICopilot.AiRuntime` model endpoint pool scheduling.
- Shared runtime contracts for caller context and safe model-pool snapshots.
- Backend tests for M3.1 runtime behavior.
- M3.1 scope guard.

## Implemented Runtime Behavior

- Endpoint pool acquisition now uses leases so selected endpoint capacity remains held until the runtime agent scope is disposed.
- Endpoint concurrency, per-model concurrency, queue limit, and queue wait timeout are enforced in memory.
- Endpoint RPM and estimated TPM windows are enforced in memory.
- Per-user, per-role, and per-tenant request windows can be enforced from `AgentRuntimeCallerContext`.
- Runtime snapshots expose queue length, in-flight count, model in-flight count, success/failure counts, average/p95 duration, rate-limit counts, circuit state, fallback count, and sticky streaming count.
- Endpoint circuit state opens after configured failures and recovers after the configured open window.
- Endpoint credentials can be resolved from a protected configured value or an environment variable name at runtime.

## Security Boundary

- No real provider endpoint/token/API key/connection string is committed.
- No `appsettings*.json` file is changed.
- Runtime snapshots do not expose API key values or raw endpoint URLs.
- `ModelEndpointDto.BaseUrl` remains present for compatibility but returns `[redacted-endpoint]` when a base URL exists.
- M3.1 does not enable real Pilot execution, M7, Cloud write, Recipe/version access, free SQL, `query_cloud_data_readonly`, or GA.

## Validation

Run:

```powershell
git diff --check
pwsh -ExecutionPolicy Bypass -File ./scripts/Test-TextEncoding.ps1
pwsh -ExecutionPolicy Bypass -File ./scripts/Test-AICopilotM3_1ModelApiPoolRuntimeScope.ps1
dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj --no-restore
dotnet build src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName~ModelProviderReliabilityTests|FullyQualifiedName~ModelSecretRuntimeBoundaryTests|FullyQualifiedName~AICopilotM3_1ModelPoolRuntimeTests"
```

## Remaining Work

- Exact prompt token accounting remains future work.
- Frontend operational dashboard polish remains a later UI PR.
- Durable distributed counters are future production hardening if the API is scaled beyond one process.
- Real provider endpoint/token configuration remains blocked until a later explicitly authorized stage.
