# AICopilot M5 Enterprise Data Source Platformization

## Stage Result

M5 closes the enterprise data source platformization loop for the current readiness baseline. The implementation hardens the existing BusinessDatabase, DataSourcePermissionGrant, readonly query, plugin, and Text-to-SQL paths. It does not introduce a new gateway and does not change Cloud or Edge.

This stage remains planning/readiness/runtime-governance only. It does not authorize real Pilot execution, M7 execution, real endpoint/token configuration, Cloud write, Recipe/version access, free SQL, `query_cloud_data_readonly` enablement, or GA.

## Implemented Scope

- Data source selection is now mode-aware. Chat mode returns only enabled, readonly, authorized, chat-selectable sources. Agent mode returns only enabled, readonly, authorized, agent-selectable SimulationBusiness sources.
- Direct readonly query execution now requires a governed semantic schema. The current executable schema is SimulationBusiness only; CloudReadOnly remains blocked until a future separately approved governed schema stage.
- SQL governance rejects non-SELECT statements, multiple statements, wildcard projections, system catalog access, DDL/DML, sensitive fields, and tables outside the source allowlist.
- Business query results now include safe governance metadata and bounded sanitized preview rows. Sensitive column names are replaced with safe redacted column names, sensitive values are redacted, and redacted column identifiers are represented only by hashes.
- Query audit records only safe summaries: query hash, SQL length, data source id, source mode, selection mode, row count, truncation flag, duration, and warning code. Query text, SQL text, raw rows, payload, provider response, tokens, API keys, secrets, and connection strings are not recorded.
- Text-to-SQL draft output exposes SQL hash and a redacted preview marker only. The executable SQL is held in the in-memory draft store and execution requires a governed draft id; direct free SQL preview execution is rejected.
- The public raw readonly SQL endpoint is retained only as a high-permission governed SQL operations API. It requires `DataSource.QueryGovernedSql`, always records `DataSourceSelectionMode.GovernedSql`, and is not part of ordinary Text-to-SQL user access.
- Text-to-SQL draft and draft-id execution require `DataSource.TextToSql`. The default `User` role does not receive either `DataSource.TextToSql` or `DataSource.QueryGovernedSql`; access must be explicitly granted through role permissions plus per-source grants.

## Interface Notes

- `DataSourceSelectionMode` was added to internal shared contracts for Chat, Agent, Query, TextToSql, and GovernedSql execution contexts.
- `BusinessQueryResultDto` keeps its existing row shape and adds optional `BusinessQueryGovernanceDto` metadata for sanitized-preview status, warning codes, redacted column hashes, and allowed tables.
- Existing read-only operational APIs remain read-only. No Cloud API, Edge API, frontend route, appsettings value, secret, endpoint, token, or database migration was added.

## Remaining Gaps

- Durable schema sync and editable semantic schema remain future work. This PR uses the existing SimulationBusiness schema as the only executable governed schema.
- CloudReadOnly governed schema, credential wiring, and execution authorization remain blocked and must be handled in a separate approved stage.
- Frontend data source selector remains deferred to a future UI PR.
- Exact prompt token accounting and full DLP classification are outside M5 and remain candidates for M6 security/compliance hardening.

## Validation

Required validation for this stage:

- `git diff --check`
- `pwsh -ExecutionPolicy Bypass -File ./scripts/Test-TextEncoding.ps1`
- `pwsh -ExecutionPolicy Bypass -File ./scripts/Test-AICopilotM5EnterpriseDataSourceScope.ps1`
- `dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj --no-restore`
- `dotnet build src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Suite=AICopilotM5EnterpriseDataSourcePlatformization|Suite=DataSourceAuthorization|Suite=DataAnalysisReadOnlyGuardrail|Suite=TextToSqlReadOnly|Suite=SqlGuardrail|Suite=EnterpriseDataGovernanceP0|Suite=EnterpriseDataGovernanceP1"`

## Next Stage Entry

Next work should remain split into separate PRs: durable M5.2 schema sync if needed, M6 security/compliance hardening, M7 dry-run, M7 limited real Pilot, and Internal GA Candidate. None of those are authorized by this M5 stage.
