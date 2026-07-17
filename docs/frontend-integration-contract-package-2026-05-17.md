# AICopilot Frontend Integration Contract Package

Date: 2026-05-17

This document is the backend-owned frontend integration contract. Frontend code must map every backend problem code below to an explicit user-facing message. New backend codes must be added here in the same change that introduces them.

For structured chat error chunks, frontend displays `userFacingMessage` first, then `detail`, and only then a code-specific fallback message.

For HTTP `ProblemDetails`, `extensions.code` and `extensions.traceId` are reserved. The backend copies ordinary descriptor extensions first, then overwrites these reserved keys with `ApiProblemDescriptor.Code` and the current `HttpContext.TraceIdentifier`; descriptor extensions cannot spoof either value.

## Auth Problem Codes

| Code | Meaning |
| --- | --- |
| `account_disabled` | Account is disabled. |
| `session_revoked` | Current session was revoked. |
| `user_missing` | Current user record is missing. |
| `missing_permission` | Current user lacks the required permission. |
| `invalid_credentials` | Login credentials are invalid. |
| `unauthorized` | Request is not authenticated. |
| `cloud_oidc_not_configured` | Cloud OIDC login is not configured. |
| `cloud_oidc_invalid_principal` | Cloud OIDC principal is invalid. |
| `cloud_identity_inactive` | Bound Cloud identity is inactive. |
| `cloud_identity_unverified` | Cloud identity is not verified. |
| `external_identity_conflict` | External identity conflicts with an existing binding. |
| `last_enabled_admin_required` | The requested identity mutation would remove the last enabled administrator; show `userFacingMessage`, keep the authenticated session, and do not retry automatically. |

## App Problem Codes

| Code | Meaning |
| --- | --- |
| `request_validation_failed` | Request validation failed before handler execution. |
| `internal_server_error` | Unexpected server failure was handled by the global exception boundary; frontend should show the safe detail and trace identifier. |
| `persistence_commit_outcome_unknown` | A write may already have committed but its durable marker could not be verified. Frontend must show the safe detail plus trace/commit identifiers and must not automatically retry the write. |
| `rate_limit_exceeded` | Rate limit was exceeded. |
| `chat_context_expired` | Chat context expired. |
| `chat_configuration_missing` | Chat runtime configuration is missing. |
| `chat_stream_failed` | Chat stream failed. |
| `model_provider_unavailable` | Model provider request failed because the provider is unavailable, unreachable, rate-limited, or returning transient server errors. |
| `model_request_timeout` | Model provider did not return a response before the configured response timeout. |
| `approval_stream_failed` | Approval stream failed. |
| `approval_already_processed` | Approval was already processed. |
| `approval_pending` | Approval is pending. |
| `capability_not_allowed` | Requested capability is not allowed. |
| `control_action_blocked` | Control or write action was blocked. |
| `token_budget_exceeded` | Token budget was exceeded. |
| `onsite_presence_required` | Onsite presence is required. |
| `onsite_presence_expired` | Onsite presence proof expired. |
| `approval_reconfirmation_required` | Approval needs reconfirmation. |
| `tool_not_registered` | Tool is not registered. |
| `tool_disabled` | Tool is disabled. |
| `tool_blocked` | Tool was blocked by policy. |
| `tool_permission_denied` | Current user lacks tool permission. |
| `tool_requires_approval` | Tool execution requires approval. |
| `tool_input_invalid` | Tool input is invalid. |
| `tool_execution_timeout` | Tool execution timed out. |
| `cloud_readonly_tool_disabled` | Cloud read-only tool is disabled. |
| `cloud_readonly_intent_unsupported` | Cloud read-only intent is unsupported. |
| `planner_model_unavailable` | Planner model is unavailable. |
| `planner_tool_catalog_empty` | Planner tool catalog is empty. |
| `planner_tool_schema_unsupported` | Planner tool schema is unsupported. |
| `agent_skill_selection_required` | Agent plan requires a selected or auto-routed Skill. |
| `agent_plan_invalid` | Agent plan is invalid. |
| `plan_payload_too_large` | Canonical Plan v2 payload exceeds the fixed 262,144-byte UTF-8 limit and was not persisted. |
| `agent_plan_tool_denied` | Agent plan requested a denied tool. |
| `agent_plan_schema_invalid` | Agent plan schema is invalid. |
| `tool_execution_not_found` | Tool execution record was not found. |
| `artifact_finalized` | Artifact is finalized and cannot be modified. |
| `artifact_generation_failed` | Artifact generation failed. |
| `workspace_manifest_invalid` | Workspace manifest is invalid. |
| `agent_task_run_in_progress` | Agent task run is already in progress. |
| `agent_task_retry_not_allowed` | Agent task retry is not allowed. |
| `agent_task_run_lease_expired` | Agent task run lease expired. |
| `agent_task_cancellation_requested` | Agent task cancellation was requested. |
| `agent_task_run_queued` | Agent task run was queued. |
| `agent_task_run_queue_not_found` | Agent task run queue item was not found. |
| `agent_task_run_queue_lease_expired` | Agent task run queue lease expired. |
| `agent_worker_unavailable` | Agent worker is unavailable. |
| `agent_worker_workspace_mismatch` | Agent worker workspace does not match HttpApi workspace. |
| `agent_run_queue_dead_letter_not_allowed` | Agent run queue dead-letter operation is not allowed. |
| `agent_run_queue_operation_denied` | Agent run queue operation is denied. |

## Cloud AI Read Problem Codes

| Code | Meaning |
| --- | --- |
| `cloud_ai_read_not_configured` | Cloud AI read is not configured. |
| `cloud_ai_read_request_blocked` | Cloud AI read request was blocked by endpoint policy. |
| `cloud_ai_read_invalid_request` | Cloud AI read rejected parameters that are outside the formal endpoint contract. |
| `cloud_ai_read_unauthorized` | Cloud AI read request was not authenticated. |
| `cloud_ai_read_forbidden` | Cloud AI read request is forbidden. |
| `cloud_ai_read_not_found` | Cloud AI read resource was not found. |
| `cloud_ai_read_rate_limited` | Cloud AI read rate limit was reached; retry later without changing data source. |
| `cloud_ai_read_unavailable` | Cloud AI read service is unavailable. |
| `cloud_ai_read_missing_required_parameter` | Cloud AI read request is missing a required parameter such as `deviceId`, `startDate`, `endDate`, `startTime`, `endTime`, `preset`, `date`, `typeKey`, or `processId`. |

## Cloud Read-Only Query Contract

Cloud read-only requests must use the current Cloud API contract directly:

| Scenario | Path | Required query |
| --- | --- | --- |
| Devices | `/api/v1/ai/read/devices` | `maxRows`, optional `deviceId`, `deviceCode`, `processId`, `keyword`; supplied conditions are ANDed |
| Processes | `/api/v1/ai/read/processes` | `maxRows`, optional `processId`, `keyword` |
| Client releases | `/api/v1/ai/read/client-releases` | `maxRows`, optional `channel`, `targetRuntime`, `status`, `includeArchived` |
| Device client states | `/api/v1/ai/read/device-client-states` | `maxRows`, optional `deviceId`, `deviceCode`, `processId`, `keyword` |
| Capacity summary | `/api/v1/ai/read/capacity/summary` | `deviceId`, `startDate`, `endDate`, `maxRows` |
| Capacity hourly | `/api/v1/ai/read/capacity/hourly` | `deviceId`, `date` or `preset`, optional `plcName`, `maxRows` |
| Device logs | `/api/v1/ai/read/device-logs` | `deviceId`, `startTime`/`endTime` or `preset`, optional `level` or `minLevel`, optional `keyword`, `maxRows` |
| Production records | `/api/v1/ai/read/production-records` | one of `typeKey`/`processId`/`deviceId`, `startTime`/`endTime` or `preset`, optional `barcode`, `result`, `fieldMode`, `maxRows`; rows may include `fieldSchema` and `fields` for plugin/process-specific columns |

Provider success responses use the exact envelope `items/asOfUtc/source/queryScope/rowCount/truncated/nextCursor`. Missing or mistyped metadata is a provider-contract failure; clients must not manufacture timestamps or truncation metadata.

`deviceCode` is never sent as `deviceId`. Device status sends it directly as the formal `deviceCode` state parameter; log/capacity/production semantic queries may first resolve it uniquely through `/devices`, then send the resulting GUID as `deviceId`. Recipe master data and recipe version data are outside the AICopilot Cloud read-only boundary. Every covered semantic domain is Cloud-only: a success, legitimate empty set, 400/401/403/429/5xx, timeout, or invalid JSON must not trigger Direct DB, Text-to-SQL, Simulation, MCP, or a hidden HTTP fallback.
