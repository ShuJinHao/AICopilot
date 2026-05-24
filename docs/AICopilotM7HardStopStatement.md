# AICopilot M7 Hard Stop Statement

Version: 2026-05-24

## Statement

M7 real Pilot remains hard-stopped.

The current repository state allows planning, readiness, and dry-run material only. It does not allow real Pilot execution, real endpoint/token configuration, production Cloud reads, Cloud writes, Recipe/version access, free SQL, or GA.

## Current Gate

- `ExecutionPermission=not granted`.
- `GateState=BlockedUntilExplicitM7Authorization`.
- Planning approval is not execution approval.
- Credential-window planning is not credential configuration approval.
- Limited-pilot-execution planning is not real Pilot execution approval.
- Dry-run readiness is not real Pilot execution.
- GPT/5.5 Pro review evidence is not M7 authorization.

## Required Standalone Authorization Before M7

Before any real Pilot work can begin, a later standalone authorization must define and approve:

- Business scope, department, owner, and approved Pilot users.
- Execution window and rollback window.
- Data owner, tool owner, output owner, rollback owner, and emergency owner.
- Endpoint allowlist and data boundary.
- Credential owner and approved secret storage path, without writing secrets into the repository.
- Emergency stop drill and rollback drill.
- Post-run audit archive format.

## Hard Stop Conditions

M7 must remain stopped if any request requires:

- Real Pilot execution without standalone authorization.
- Real endpoint/token/API key/connection string entry into repository files.
- Cloud write.
- Recipe or Recipe version access.
- Free SQL.
- `query_cloud_data_readonly` enablement.
- Raw payload, full SQL, raw business rows, token, API key, or connection string output.
- Bypassing Human-in-the-loop.
- Bypassing emergency stop.
- Bypassing Tool Registry.
- Declaring GA.
- Modifying `IIoT.CloudPlatform` or `IIoT.EdgeClient` without explicit standalone approval.

## Allowed Current Work

Allowed current work is limited to documentation, planning, readiness evidence, and dry-run preparation inside `AICopilot` only.

Any later request that crosses this boundary must stop and obtain explicit standalone authorization before implementation.
