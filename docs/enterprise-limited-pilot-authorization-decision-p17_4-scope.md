# AICopilot P17.4 Scope Freeze

## Stage Position

P17.4 is the limited real Pilot authorization decision intake and execution window freeze stage.

This stage does not execute a real Pilot, does not configure a real endpoint, does not read or write a real credential, and is not GA.

## Allowed Work

- Record the user authorization decision state for future limited Pilot execution planning.
- Freeze the Pilot Window draft, user scope, approval chain, rollback owner, emergency stop owner, endpoint allowlist, and data boundary.
- Freeze credential configuration responsibility as a checklist only.
- Generate a sanitized P17.4 acceptance report.

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

P17.4 defaults to `AuthorizationPending`.

`AuthorizationGrantedForPlanning` can only be recorded after the user explicitly approves future execution planning in a separate instruction. Even then, P17.4 still does not execute a real Pilot.

## Safety Boundary

`query_cloud_data_readonly` remains disabled, hidden, and non-executable. Future execution still requires a separate approved execution stage with a named Pilot Window, approval chain, runtime credential configuration confirmation, rollback window, emergency stop validation, and execution owner confirmation.
