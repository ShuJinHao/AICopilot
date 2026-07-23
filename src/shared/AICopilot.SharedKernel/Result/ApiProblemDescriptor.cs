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

public static partial class AppProblemCodes
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
