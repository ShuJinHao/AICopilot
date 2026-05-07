# AICopilot Main Acceptance Report - 2026-05-07

## Summary

Current `main` is a valid conditional closeout baseline for AICopilot. The merged work keeps AICopilot scoped to read, analyze, explain, summarize, retrieve, and recommend; it does not introduce Cloud or Edge code changes and does not add Cloud write paths.

## Merged PRs

| Commit | Change |
| --- | --- |
| `d5ef4f2` | Frontend protocol unit test foundation |
| `716f82d` | Provider reliability v2 |
| `2c7032e` | RAG document governance v1 |

## Verification

All checks were run on `main` on 2026-05-07.

| Command | Result |
| --- | --- |
| `dotnet test src\tests\AICopilot.ArchitectureTests\AICopilot.ArchitectureTests.csproj` | Passed: 43 |
| `dotnet test src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj --filter "Runtime\|Rag\|AiEval\|Mcp\|ToolSafety\|Approval\|DataAnalysis"` | Passed: 82 |
| `dotnet test src\tests\AICopilot.AiEvalTests\AICopilot.AiEvalTests.csproj` | Passed: 6 |
| `cd src\vues\AICopilot.Web && npm run test:unit` | Passed: 12 |
| `cd src\vues\AICopilot.Web && npm run build` | Passed |
| `cd src\vues\AICopilot.Web && npm run test:smoke` | Passed: 13, skipped: 1 |
| `git diff --check` | Passed |

## Acceptance Notes

- Cloud/Edge were not modified as part of this closeout sequence.
- Existing untracked items remain intentionally excluded: `.claude/`, root `AICopilot/`, and `REVIEW_FOLLOWUP_2026-04-29.md`.
- RAG governance migration is `20260507035115_AddRagDocumentGovernance`.
- RAG document defaults preserve existing behavior: `Internal`, `UserUploaded`, `AllowedForFinalPrompt=true`.
- RAG retrieval excludes documents that are forbidden, disallowed for final prompt, not yet effective, or expired.
- Provider fallback remains disabled by default and high-risk tool/approval/DataAnalysis SQL chains remain non-fallback paths.

## Remaining Follow-up

- Add RAG governance metadata editing for uploaded documents.
- Add Provider Reliability read-only configuration visibility in the admin UI.
- Keep Workflow Graph/Planner, long-term memory, and Cloud write integrations out of scope unless explicitly approved later.
