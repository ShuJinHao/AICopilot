namespace AICopilot.SharedKernel.Result;

public static class AuthProblemCodes
{
    public const string AccountDisabled = "account_disabled";
    public const string SessionRevoked = "session_revoked";
    public const string UserMissing = "user_missing";
    public const string MissingPermission = "missing_permission";
    public const string InvalidCredentials = "invalid_credentials";
    public const string Unauthorized = "unauthorized";
    public const string CloudOidcNotConfigured = "cloud_oidc_not_configured";
    public const string CloudOidcInvalidPrincipal = "cloud_oidc_invalid_principal";
    public const string CloudIdentityInactive = "cloud_identity_inactive";
    public const string CloudIdentityUnverified = "cloud_identity_unverified";
    public const string ExternalIdentityConflict = "external_identity_conflict";
    public const string LastEnabledAdminRequired = "last_enabled_admin_required";
}

public static class AppProblemCodes
{
    public const string InternalServerError = "internal_server_error";
    public const string PersistenceCommitOutcomeUnknown = "persistence_commit_outcome_unknown";
    public const string RequestValidationFailed = "request_validation_failed";
    public const string RateLimitExceeded = "rate_limit_exceeded";
    public const string ChatContextExpired = "chat_context_expired";
    public const string ChatConfigurationMissing = "chat_configuration_missing";
    public const string ChatStreamFailed = "chat_stream_failed";
    public const string ModelProviderUnavailable = "model_provider_unavailable";
    public const string ModelRequestTimeout = "model_request_timeout";
    public const string ApprovalStreamFailed = "approval_stream_failed";
    public const string ApprovalAlreadyProcessed = "approval_already_processed";
    public const string AgentApprovalStateConflict = "agent_approval_state_conflict";
    public const string AgentApprovalRejected = "agent_approval_rejected";
    public const string ApprovalPending = "approval_pending";
    public const string CapabilityNotAllowed = "capability_not_allowed";
    public const string ControlActionBlocked = "control_action_blocked";
    public const string TokenBudgetExceeded = "token_budget_exceeded";
    public const string OnsitePresenceRequired = "onsite_presence_required";
    public const string OnsitePresenceExpired = "onsite_presence_expired";
    public const string ApprovalReconfirmationRequired = "approval_reconfirmation_required";
    public const string ToolNotRegistered = "tool_not_registered";
    public const string ToolDisabled = "tool_disabled";
    public const string ToolBlocked = "tool_blocked";
    public const string ToolPermissionDenied = "tool_permission_denied";
    public const string ToolRequiresApproval = "tool_requires_approval";
    public const string ToolInputInvalid = "tool_input_invalid";
    public const string ToolOutputSchemaInvalid = "tool_output_schema_invalid";
    public const string ToolExecutionTimeout = "tool_execution_timeout";
    public const string CloudReadonlyToolDisabled = "cloud_readonly_tool_disabled";
    public const string CloudReadonlyIntentUnsupported = "cloud_readonly_intent_unsupported";
    public const string PlannerToolSchemaUnsupported = "planner_tool_schema_unsupported";
    public const string AgentPlanInvalid = "agent_plan_invalid";
    public const string PlanPayloadTooLarge = "plan_payload_too_large";
    public const string EvidencePayloadTooLarge = "evidence_payload_too_large";
    public const string AgentPlanToolDenied = "agent_plan_tool_denied";
    public const string AgentPlanSchemaInvalid = "agent_plan_schema_invalid";
    public const string ToolExecutionNotFound = "tool_execution_not_found";
    public const string ArtifactFinalized = "artifact_finalized";
    public const string ArtifactGenerationFailed = "artifact_generation_failed";
    public const string WorkspaceManifestInvalid = "workspace_manifest_invalid";
    public const string AgentTaskRunInProgress = "agent_task_run_in_progress";
    public const string AgentTaskRetryNotAllowed = "agent_task_retry_not_allowed";
    public const string AgentTaskRunLeaseExpired = "agent_task_run_lease_expired";
    public const string AgentTaskCancellationRequested = "agent_task_cancellation_requested";
    public const string AgentTaskRunQueued = "agent_task_run_queued";
    public const string AgentTaskRunQueueNotFound = "agent_task_run_queue_not_found";
    public const string AgentTaskRunQueueLeaseExpired = "agent_task_run_queue_lease_expired";
    public const string AgentTaskRunFenceStale = "agent_task_run_fence_stale";
    public const string AgentNodeRunFenceStale = "agent_node_run_fence_stale";
    public const string AgentNodeRunStateConflict = "agent_node_run_state_conflict";
    public const string AgentRunBudgetExceeded = "agent_run_budget_exceeded";
    public const string AgentWorkerUnavailable = "agent_worker_unavailable";
    public const string AgentWorkerWorkspaceMismatch = "agent_worker_workspace_mismatch";
    public const string AgentFinalizationStateConflict = "agent_finalization_state_conflict";
    public const string AgentRunQueueDeadLetterNotAllowed = "agent_run_queue_dead_letter_not_allowed";
    public const string AgentRunQueueOperationDenied = "agent_run_queue_operation_denied";
}

public static class ApiProblemExtensionKeys
{
    public const string Code = "code";
    public const string MissingPermissions = "missingPermissions";
    public const string RetryAfterSeconds = "retryAfterSeconds";
    public const string UserFacingMessage = "userFacingMessage";
    public const string TraceId = "traceId";
}

public sealed record ApiProblemDescriptor(
    string Code,
    string Detail,
    IReadOnlyDictionary<string, object?>? Extensions = null);
