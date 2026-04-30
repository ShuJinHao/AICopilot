# AICopilot Repository Rules

## Project Positioning

`AICopilot` is an independent repository in this workspace.
- It is not the cloud repository and not the edge repository.
- When the task is about `AICopilot`, default to modifying only `AICopilot`.
- Do not modify `IIoT.CloudPlatform` and `IIoT.EdgeClient` in the same task unless the user explicitly approves it.
- `AICopilot` may reference cloud business semantics, but it may not silently rewrite cloud business definitions.

## Source Of Truth

For business semantics, the current cloud business is the reference source.
- AI-side permissions, device concepts, process concepts, and upload semantics should align to the actual cloud business that already exists.
- If the cloud side has no confirmed business rule yet, do not invent one on the AI side.
- If the AI-side logic conflicts with the cloud-side confirmed business, report the conflict first and discuss it with the user.
- `AICopilot` is an assistant/orchestration system, not the master source of manufacturing business data.

## Cloud Business Read-only Boundary

`AICopilot` may consume confirmed cloud business data for analysis, explanation, summarization, retrieval, and recommendation only.
- It must not directly write to the Cloud database.
- It must not create, update, delete, backfill, approve, dispatch, or trigger Cloud business records or Cloud business workflows.
- It must not use MCP tools, agent workflows, background jobs, direct SQL, or hidden adapters to call Cloud write APIs indirectly.
- Human-in-the-loop approval is not permission to turn AICopilot into a Cloud business write entrypoint.
- If the CloudPlatform later exposes explicit AI-facing APIs, AICopilot may call only those APIs that Cloud intentionally designed for AI usage, with explicit permission, audit, and interface contracts.
- Until such Cloud AI-facing APIs exist and are approved, all Cloud alignment work in AICopilot must remain read-only.

## Current Architecture

The current repository structure already follows the same broad direction as the cloud-side layered architecture:
- `src/core`: domain core, aggregates, entities, value objects, domain rules.
- `src/services`: commands, queries, workflows, application orchestration, MediatR handlers.
- `src/infrastructure`: EF Core, Dapper, embedding, event bus, provider integration, external technology details.
- `src/hosts`: composition root, API, worker, app host, startup wiring.
- `src/shared`: shared kernel, contracts, plugin abstractions, cross-cutting shared components.
- `src/vues`: frontend only.

This structure must continue to align with the cloud-side DDD layering.

## Architecture Rules

`AICopilot` must be written in a way that stays aligned with the cloud-side framework discipline.
- Follow DDD layering and dependency inversion.
- Keep domain rules in `Core`.
- Keep application orchestration, workflows, commands, and queries in `Services`.
- Keep database, SDK, embedding, event bus, MCP protocol, and other technology details in `Infrastructure`.
- Keep `Hosts` thin. `Hosts` may wire services, expose APIs, and host workers, but must not absorb core business logic.
- Keep controllers thin. Controllers should forward requests and return results, not hold business logic.
- Keep `Shared` focused on truly shared abstractions and shared kernel concerns.
- Keep frontend logic inside `src/vues`, not backfill it into service or host layers.
- Treat AICopilot Identity user identifiers as `Guid` semantics. `ICurrentUser.Id` and internal identity access contracts must use Guid-typed values; do not add scattered `Guid.Parse(currentUser.Id)` string parsing patterns.

## Capability Boundary Rules

The following business capabilities are important and must remain clearly separated:
- Intent routing.
- RAG knowledge retrieval.
- Data analysis and Text-to-SQL.
- MCP tool execution.
- Human-in-the-loop approval.

Do not collapse these capabilities into one giant agent or one giant service just because it seems faster.
- Routing decides what to call.
- RAG retrieves knowledge.
- Data analysis handles structured analysis.
- MCP handles tool execution.
- Human-in-the-loop controls risky execution approval.

If one feature touches multiple capabilities, explain the boundary impact first before changing it.

## Package Rules

Preview NuGet packages are forbidden by default.
- Do not add preview, prerelease, alpha, beta, rc, or nightly NuGet packages.
- If there is no stable package available, stop and discuss it with the user before adding or upgrading the dependency.
- If the repository already contains preview packages, treat them as existing debt, not as permission to keep expanding preview usage.
- Do not upgrade one preview package to another preview package without explicit user approval.

Known-vulnerable dependencies are forbidden unless an explicit exception is documented and approved.
- Do not add NuGet, npm, container image, or tool dependencies with active vulnerability advisories.
- `NU190x`, `npm audit`, GitHub advisory, Dependabot, and container scan findings must be treated as blockers by default.
- If a vulnerable dependency cannot be upgraded immediately, record the package, version, advisory, impact, mitigation, owner, and expiry date before merging related work.

## Scope Rules

`AICopilot` is currently developed separately from cloud and edge.
- It may reference cloud behavior and cloud business rules for alignment.
- It may not modify cloud code unless the user explicitly allows it.
- It may not modify edge code unless the user explicitly allows it.
- It may not modify AI, cloud, and edge together in one task unless the user explicitly asks for coordinated work.

## Refactor Rules

`AICopilot` currently contains earlier testing and experimental functionality.
- Internal AI-side modules may be refactored.
- Old testing-oriented AI implementation is not automatically protected as long-term stable structure.
- Refactoring inside `AICopilot` is allowed when it helps align the project to the current target architecture.

Even with that flexibility, the following still apply:
- Do not use refactoring as an excuse to break the layered structure.
- Do not turn a focused change into an uncontrolled repository-wide rewrite.
- If a refactor changes capability boundaries, approval flow, persistence flow, or multiple subdomains at once, discuss it first.

## Business Alignment Rules

AI-side business should align to the actual current AI system design and the confirmed cloud-side business.
- Intent routing is the dispatch brain, not the place to hardcode every downstream implementation detail.
- MCP execution is a controlled execution arm, not an unrestricted shell.
- Human-in-the-loop is a hard safety gate for risky actions, not optional decoration.
- If an action may write files, change data, or trigger external side effects, treat approval as required unless the user has clearly defined it as safe and exempt.
- If it is unclear whether an action is high-risk, ask the user before implementing or relaxing the approval gate.

## Configuration And Security Rules

Do not hardcode business-configurable or sensitive information into implementation code.
- Do not hardcode API keys, tokens, secrets, license strings, provider credentials, database credentials, or MCP credentials.
- Do not hardcode model provider endpoints or environment-specific values when they should be configurable.
- Model definitions, prompt templates, plugin registration, MCP server settings, approval thresholds, and similar runtime behavior should prefer clear configuration or explicit stored data rather than hidden hardcoded logic.
- If you find existing hardcoded secrets, credentials, or license-like values, treat them as historical debt and discuss cleanup before spreading the pattern.

## Writing Rules

Code quality rules are strict.
- Code must stay sufficiently decoupled.
- Do not create tight coupling between workflow code, tool execution code, data access code, and host startup code.
- Keep naming clear and close to business meaning.
- Comments must be clear and useful for humans.
- Do not write placeholder comments or empty comments.
- Add comments where workflow branching, approval gates, tool permissions, retry logic, or safety constraints are not obvious from the code itself.

## Execution Rules

Before making changes in `AICopilot`, check the following:
- Which capability is being touched: routing, RAG, SQL analysis, MCP, approval, host wiring, frontend, or persistence.
- Whether the change is only inside `AICopilot`, or whether it would imply cloud or edge changes.
- Whether the requested business behavior already exists on the cloud side.
- Whether the change would introduce or rely on preview dependencies.

If business behavior is unclear, ask the user instead of guessing.

Even in plan mode:
- Do not output a formal plan immediately.
- Discuss business alignment and architecture alignment first.
- Only generate a formal plan when the user explicitly asks to generate the plan.

## Review And Branch Rules

Large `AICopilot` changes must follow the same delivery discipline as the rest of the workspace.
- After a large change, prepare the change for GitHub review instead of silently stopping at local edits.
- If review feedback reveals business, architecture, or safety concerns, discuss them with the user before applying the fix.
- After review is complete and the branch is merged, delete the completed branch unless the user explicitly wants to keep it.

## Current Debt Note

The repository currently contains signs of earlier experimental construction.
- Existing preview-package usage does not authorize new preview-package usage.
- Existing hardcoded sensitive-looking values do not authorize new hardcoded sensitive values.
- Earlier test-oriented implementation does not force future work to preserve the same shape if the user wants it cleaned up.
