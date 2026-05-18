# A Assistant Agent Runtime Batch 5 Approval Hardening Report

- Date: 2026-05-18
- Scope: AICopilot backend Batch 5 only
- Cloud/Edge touched: No
- Frontend touched: No
- Real Cloud access introduced: No
- Shell capability introduced: No
- Arbitrary server path write introduced: No
- Database migration introduced: No

## Completed

- Added dedicated approval permissions:
  - `AiGateway.ApproveAgentToolCall`
  - `AiGateway.ApproveFinalOutput`
- Removed `AiGateway.FinalizeWorkspace` from the default `User` role.
- Kept `User` able to create tasks, approve own Plan, run tasks, upload files, view owned workspace/artifacts, download owned artifacts, and submit final review.
- Enforced approval decisions by approval type:
  - `Plan` requires `AiGateway.ApproveAgentTaskPlan` and remains owner-scoped.
  - `ToolCall` requires `AiGateway.ApproveAgentToolCall`.
  - `Artifact` and `FinalOutput` require `AiGateway.ApproveFinalOutput`.
- Allowed privileged ToolCall/FinalOutput approvers to process corresponding approvals across users.
- Allowed FinalOutput approvers and finalize operators to read required cross-user workspace/artifact content for review, limited to workspaces that already have a FinalOutput approval record.
- Kept finalize gated by both `AiGateway.FinalizeWorkspace` and an approved FinalOutput approval.
- Updated task DTO capability calculation so `canApproveFinal` is not reported true for users lacking `AiGateway.ApproveFinalOutput`.

## Verification

- `dotnet build src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug`
  - PASS
- `dotnet test src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=Batch5ApprovalHardening"`
  - PASS: 2 tests
- `dotnet test src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "FullyQualifiedName~FreshDatabaseSeedTests|FullyQualifiedName~IdentityAccessManagementTests"`
  - PASS: 9 tests
- `dotnet test src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "FullyQualifiedName~ToolRegistryGovernanceTests"`
  - PASS: 42 tests
- `.\scripts\Run-AgentSimulationAcceptance.ps1`
  - PASS: scope guard
  - PASS: backend build
  - PASS: Simulation unit tests
  - PASS: Simulation Docker acceptance

## Remaining Risk

- Existing frontend dirty files were not modified; the frontend may still need a later UI update to represent the stricter approval permissions cleanly.
- Existing databases that already synced the old `User` role must run the existing migration/bootstrap sync path to remove the old `AiGateway.FinalizeWorkspace` grant.
