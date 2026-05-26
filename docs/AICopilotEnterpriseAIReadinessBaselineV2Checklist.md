# AICopilot Enterprise AI Readiness Baseline v2 Checklist

Version: 2026-05-26

## Baseline Acceptance

- [x] `AICopilot Enterprise AI Readiness Baseline v2` is accepted as readiness and governance closure only.
- [x] PR #55 has been merged into `main` at merge commit `6c0b9cd`.
- [x] M5.1 raw SQL and Text-to-SQL permission boundary is included in the current baseline.
- [x] Current status is not real Pilot execution.
- [x] Current status is not production rollout approval.
- [x] Current status is not GA.
- [x] Follow-up productionization and hardening work remains split into later tasks.

## Required Evidence

- [x] M1 enterprise readiness baseline evidence exists.
- [x] M2 Pilot Authorization Workflow is implemented and documented.
- [x] M2.1 hardening covers sensitive draft blocking, self-review prohibition, expiration lifecycle, and M7 material intake.
- [x] Batch 5-10 readiness covers audit timeline, sensitive content guard enhancement, design freeze evidence, and M7 dry-run readiness.
- [x] M5 enterprise data source platformization is documented after PR #55.
- [x] M7 real Pilot hard stop remains active.
- [x] M7 dry-run/readiness evidence is explicitly not execution.

## M5 Boundary Checks

- [x] Raw readonly SQL is treated as a high-permission governed SQL operations API.
- [x] Governed SQL requires `DataSource.QueryGovernedSql`.
- [x] Text-to-SQL draft and execute require `DataSource.TextToSql`.
- [x] Text-to-SQL execution requires `DraftId`.
- [x] Default `User` role receives neither `DataSource.TextToSql` nor `DataSource.QueryGovernedSql`.
- [x] Governed SQL keeps schema enforcement, SQL guardrail, query hash audit, and sanitized preview.
- [x] Agent business database access stays on the Text-to-SQL path.
- [x] `CloudReadOnly` remains blocked for Agent and Text-to-SQL until a later approved governed schema stage.

## Hard Gates

- [x] `ExecutionPermission=not granted`.
- [x] `GateState=BlockedUntilExplicitM7Authorization`.
- [x] Planning/readiness/dry-run does not become execution permission.
- [x] GPT/5.5 Pro review evidence does not become M7 real Pilot authorization.
- [x] Real Pilot requires later standalone authorization.

## Frozen Change Checks

- [x] No `IIoT.CloudPlatform` changes.
- [x] No `IIoT.EdgeClient` changes.
- [x] No AICopilot frontend changes.
- [x] No source code changes in this v2 documentation closure.
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

- [x] `git diff --check` covered by local pre-push validation and PR #56 submitted-state validation.
- [x] `pwsh -ExecutionPolicy Bypass -File ./scripts/Test-TextEncoding.ps1` covered by local pre-push validation.
- [x] `pwsh -ExecutionPolicy Bypass -File ./scripts/Test-AICopilotM2M9GovernanceScope.ps1` covered by local pre-push validation.
- [x] `pwsh -ExecutionPolicy Bypass -File ./scripts/Test-AICopilot55TotalReviewScope.ps1` covered by local pre-push validation.
- [x] PR #56 Files changed is limited to the approved 12 review-package files.
- [x] PR #56 initial submitted head `3a46fa1` passed `AICopilot Simulation Release Candidate / simulation-rc`.
- [x] Docker simulation suites remain skipped under current CI configuration and are not claimed as full Docker integration validation.

## Completion Standard

This closure is complete only when the four v2 baseline documents exist, PR #56 review-package scope remains limited to the approved 12 files, all available validations pass, and unavailable or skipped validation such as Docker simulation suites is recorded explicitly.
