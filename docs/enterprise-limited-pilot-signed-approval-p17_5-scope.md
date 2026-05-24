# AICopilot P17.5 Scope Freeze

## Stage Position

P17.5 is the limited real Pilot signed execution approval package and final Go/No-Go gate stage.

This stage does not execute a real Pilot, does not configure a real endpoint, does not read or write a real credential, and is not GA.

## Allowed Work

- Prepare a signed execution approval package template for a future manual Pilot execution step.
- Freeze the Pilot Window, approver, executor, rollback owner, emergency stop owner, endpoint allowlist, and data boundary requirements.
- Freeze the credential configuration window as a responsibility and checklist record only.
- Generate a sanitized P17.5 acceptance report.

## Explicit Non-Scope

- Do not modify `IIoT.CloudPlatform`.
- Do not modify `IIoT.EdgeClient`.
- Do not add a Cloud endpoint.
- Do not enable Cloud write.
- Do not enable Recipe or Recipe version access.
- Do not enable free SQL.
- Do not enable or expose `query_cloud_data_readonly`.
- Do not execute a real Pilot.
- Do not configure, read, write, or display a real token, API Key, connection string, or password.
- Do not output raw payload, raw business records, full SQL, or sensitive context.

## Default Decision

P17.5 defaults to `MissingSignedExecutionApproval`.

`ReadyForManualPilotExecutionStep` can only be recorded after a complete signed approval package exists. Even then, P17.5 still does not call a real endpoint or configure a real credential.

## Safety Boundary

`query_cloud_data_readonly` remains disabled, hidden, and non-executable. Future real Pilot execution must be a later explicit stage with separate user instruction, approved Pilot Window, approved credential configuration, executor confirmation, rollback window, emergency stop online verification, and post-execution audit archival.
