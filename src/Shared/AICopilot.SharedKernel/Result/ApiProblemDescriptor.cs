namespace AICopilot.SharedKernel.Result;

public static class AuthProblemCodes
{
    public const string AccountDisabled = "account_disabled";
    public const string SessionRevoked = "session_revoked";
    public const string UserMissing = "user_missing";
    public const string MissingPermission = "missing_permission";
    public const string InvalidCredentials = "invalid_credentials";
    public const string Unauthorized = "unauthorized";
}

public static class AppProblemCodes
{
    public const string RateLimitExceeded = "rate_limit_exceeded";
    public const string ChatContextExpired = "chat_context_expired";
    public const string ChatConfigurationMissing = "chat_configuration_missing";
    public const string ChatStreamFailed = "chat_stream_failed";
    public const string ApprovalStreamFailed = "approval_stream_failed";
    public const string ApprovalAlreadyProcessed = "approval_already_processed";
    public const string ApprovalPending = "approval_pending";
    public const string CapabilityNotAllowed = "capability_not_allowed";
    public const string ControlActionBlocked = "control_action_blocked";
    public const string TokenBudgetExceeded = "token_budget_exceeded";
    public const string OnsitePresenceRequired = "onsite_presence_required";
    public const string OnsitePresenceExpired = "onsite_presence_expired";
    public const string ApprovalReconfirmationRequired = "approval_reconfirmation_required";
}

public static class ApiProblemExtensionKeys
{
    public const string MissingPermissions = "missingPermissions";
    public const string RetryAfterSeconds = "retryAfterSeconds";
}

public sealed record ApiProblemDescriptor(
    string Code,
    string Detail,
    IReadOnlyDictionary<string, object?>? Extensions = null);
