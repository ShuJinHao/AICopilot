# AICopilot M4 RAG Governance Loop

Version: 2026-05-24

## Scope

M4 closes the RAG governance loop for default retrieval and safe answer evidence. It is an AICopilot-only backend/RAG governance stage.

This stage does not change AICopilot frontend, Cloud, Edge, appsettings, migrations, runtime credentials, real endpoints, Pilot execution, M7 authorization, or GA status.

## Implemented Behavior

- Default RAG retrieval excludes documents that are not searchable, not allowed for final prompt, forbidden, soft-deleted, deleting, deleted, superseded, failed, not yet effective, expired, or blocked by category visibility.
- Default RAG retrieval uses the latest effective document version per `DocumentGroupId`.
- `CriticalOverride` and `High` knowledge supplements are placed before matched document excerpts and marked as governance overrides.
- Search results include safe governance evidence: document citation metadata, document version metadata, category reference, citation hash, supplement hash, and warning codes.
- Category visibility is enforced for enabled categories:
  - `Admin` can read all enabled categories.
  - `AuthenticatedUsers` or empty visibility is readable by authenticated users.
  - `Department` requires a match with `ICurrentUser.CloudDepartmentId` or `ICurrentUser.CloudDepartmentName`.
  - Unknown visibility is denied for non-admin users.
- RAG recall writes a safe audit summary containing query hash, knowledge base id, topK/minScore, hit document ids, supplement hashes, filtered counts, and warning codes only.

## Output Boundary

- M4 recall audit does not persist query text, chunk text, supplement content, prompt payload, provider response, raw payload, raw business rows, full SQL, token, API key, connection string, or secrets.
- M4 does not enable Cloud write, Recipe/version access, free SQL, `query_cloud_data_readonly`, real Pilot execution, real endpoint/token configuration, or GA.
- M4 does not add a database migration. If later durable RAG evidence tables are required, they must be handled in a separate migration PR.

## Validation

Run:

```powershell
git diff --check
pwsh -ExecutionPolicy Bypass -File ./scripts/Test-TextEncoding.ps1
pwsh -ExecutionPolicy Bypass -File ./scripts/Test-AICopilotM4RagGovernanceScope.ps1
dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj --no-restore
dotnet build src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Suite=RagPermission|Suite=EnterpriseDataGovernanceP0|Suite=EnterpriseDataGovernanceP1|Suite=AICopilotM4RagGovernanceLoop"
```

## Remaining Work

- M5 enterprise data source platformization remains a later separate PR.
- M6 security and compliance hardening remains a later separate PR.
- M7 dry-run and real Pilot remain blocked until their standalone authorization gates.
