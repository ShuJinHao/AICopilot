# AICopilot M6 Security And Compliance Hardening

Version: 2026-05-26

## Stage Result

M6 closes the current security and compliance hardening baseline for AICopilot readiness review. It does not introduce new Cloud or Edge integration, does not add a new runtime gateway, and does not change frontend behavior.

This stage remains readiness and runtime-governance hardening only. It does not authorize real Pilot execution, real endpoint/token configuration, Cloud write, Recipe/version access, free SQL, `query_cloud_data_readonly` enablement, or GA.

## Implemented Hardening Baseline

- Sensitive runtime values remain redacted in DTOs and snapshots. Model and embedding API keys expose only presence and fixed preview markers, and model endpoint snapshots use `[redacted-endpoint]`.
- RAG recall, Text-to-SQL, governed SQL, tool execution, artifact preview, and dry-run evidence use hashes and bounded metadata rather than raw payload, raw rows, full SQL, provider responses, tokens, API keys, secrets, or connection strings.
- Upload policy rejects dangerous extensions, spoofed MIME/content, oversized documents, and unsafe filenames before dispatch.
- Cloud-related tool safety policy rejects write semantics, side-effecting capability declarations, destructive MCP hints, non-read-only hints, and unapproved verbs.
- Artifact download requires explicit artifact download permission or final-review workspace visibility, then writes download audit metadata without storing file content.
- SQL execution remains behind `ISqlGuardrail` and `IDatabaseConnector`, read-only transactions, bounded readers, SQL hash metadata, and sanitized summaries.
- Pilot Authorization, M7 hard stop, M2-M9 scope guard, and Baseline v2 documents preserve `ExecutionPermission=not granted` and `GateState=BlockedUntilExplicitM7Authorization`.

## Compliance Boundaries

- No plaintext secret, token, API key, connection string, provider response, raw payload, raw business row, or full SQL may be written to DTOs, audit metadata, reports, logs, or review artifacts.
- No `appsettings*.json` file is changed.
- No migration is added by this M6 stage.
- No AICopilot frontend, Cloud, or Edge file is changed.
- M6 does not relax the M5 governed SQL or Text-to-SQL boundary.

## Validation

Run:

```powershell
git diff --check
pwsh -ExecutionPolicy Bypass -File ./scripts/Test-TextEncoding.ps1
pwsh -ExecutionPolicy Bypass -File ./scripts/Test-AICopilotM6SecurityComplianceScope.ps1
dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj --no-restore
dotnet build src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "Suite=AICopilotM6SecurityComplianceHardening|FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~ModelSecretContractTests|FullyQualifiedName~UploadValidationTests|FullyQualifiedName~ToolSafetyAndApprovalIdentityTests"
```

## Remaining Work

- Enterprise-wide SOC/SIEM integration, vault/KMS rotation operations, malware scanning service integration, and long-term retention automation remain future work.
- Real endpoint/token use remains blocked until a later explicitly authorized stage.
- M7 dry-run remains evidence-only and does not grant real Pilot execution.
