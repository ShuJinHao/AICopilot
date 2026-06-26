using System.Text.RegularExpressions;
using AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;

namespace AICopilot.AiGatewayService.PilotAuthorization;

internal static class PilotAuthorizationMapper
{
    public static PilotAuthorizationSubmissionDto Map(PilotAuthorizationSubmission submission)
    {
        return new PilotAuthorizationSubmissionDto(
            submission.Id.Value,
            submission.Status.ToString(),
            PilotAuthorizationSafeText.Redact(submission.Title) ?? string.Empty,
            PilotAuthorizationSafeText.Redact(submission.BusinessPurpose) ?? string.Empty,
            submission.RequestedByUserId,
            PilotAuthorizationSafeText.Redact(submission.RequestedByUserName),
            submission.EndpointCodes,
            submission.MaxRows,
            submission.TimeRangeDays,
            PilotAuthorizationSafeText.Redact(submission.DataOwner) ?? string.Empty,
            PilotAuthorizationSafeText.Redact(submission.ToolOwner) ?? string.Empty,
            PilotAuthorizationSafeText.Redact(submission.FinalOwner) ?? string.Empty,
            PilotAuthorizationSafeText.Redact(submission.RollbackPlan.RollbackOwner) ?? string.Empty,
            PilotAuthorizationSafeText.Redact(submission.RollbackPlan.EmergencyOwner) ?? string.Empty,
            submission.MachineValidationStatus,
            submission.MachineRejectedReasons,
            PilotAuthorizationSafeText.Redact(submission.EvidenceArchive.EvidenceSummary),
            PilotAuthorizationSafeText.Redact(submission.RollbackPlan.RollbackSummary),
            PilotAuthorizationSafeText.Redact(submission.MaterialIntake.BusinessScope),
            PilotAuthorizationSafeText.Redact(submission.MaterialIntake.Department),
            PilotAuthorizationSafeText.Redact(submission.MaterialIntake.PilotOwner),
            submission.MaterialIntake.ExecutionWindowStart,
            submission.MaterialIntake.ExecutionWindowEnd,
            submission.MaterialIntake.RollbackWindowStart,
            submission.MaterialIntake.RollbackWindowEnd,
            PilotAuthorizationSafeText.Redact(submission.MaterialIntake.CredentialOwner),
            PilotAuthorizationSafeText.Redact(submission.MaterialIntake.SecretStorageMode),
            PilotAuthorizationSafeText.Redact(submission.MaterialIntake.SecretReferenceNameHash),
            PilotAuthorizationSafeText.Redact(submission.MaterialIntake.PostRunAuditArchiveFormat),
            PilotAuthorizationSafeText.Redact(submission.MaterialIntake.SignedApprovalRef),
            submission.ExpiresAt,
            PilotAuthorizationSafeText.Redact(submission.CredentialWindow.PlanningSummary),
            submission.Review.LastDecisionStatus,
            PilotAuthorizationSafeText.Redact(submission.Review.LastDecisionReason),
            PilotAuthorizationGateState.Calculate(submission),
            submission.CreatedAt,
            submission.UpdatedAt);
    }
}

internal static class PilotAuthorizationGateState
{
    public const string BlockedMissingAuthorizationMaterials = "BlockedMissingAuthorizationMaterials";
    public const string BlockedUnsafeAuthorizationMaterials = "BlockedUnsafeAuthorizationMaterials";
    public const string ReviewPending = "ReviewPending";
    public const string ApprovedForCredentialWindowPlanning = "ApprovedForCredentialWindowPlanning";
    public const string ApprovedForLimitedPilotExecutionPlanning = "ApprovedForLimitedPilotExecutionPlanning";
    public const string BlockedUntilExplicitM7Authorization = "BlockedUntilExplicitM7Authorization";

    public static string Calculate(PilotAuthorizationSubmission submission)
    {
        if (!PilotAuthorizationSensitiveContentGuard.CheckSubmission(submission).IsSafe)
        {
            return BlockedUnsafeAuthorizationMaterials;
        }

        if (HasMissingAuthorizationMaterials(submission))
        {
            return BlockedMissingAuthorizationMaterials;
        }

        return submission.Status switch
        {
            PilotAuthorizationSubmissionStatus.ReviewPending => ReviewPending,
            PilotAuthorizationSubmissionStatus.ApprovedForCredentialWindowPlanning =>
                BlockedUntilExplicitM7Authorization,
            PilotAuthorizationSubmissionStatus.ApprovedForLimitedPilotExecutionPlanning =>
                BlockedUntilExplicitM7Authorization,
            _ => BlockedUntilExplicitM7Authorization
        };
    }

    private static bool HasMissingAuthorizationMaterials(PilotAuthorizationSubmission submission)
    {
        return submission.ExpiresAt is null
               || submission.EndpointCodes.Length == 0
               || submission.MaxRows is <= 0 or > 50
               || submission.TimeRangeDays is <= 0 or > 7
               || string.IsNullOrWhiteSpace(submission.DataOwner)
               || string.IsNullOrWhiteSpace(submission.ToolOwner)
               || string.IsNullOrWhiteSpace(submission.FinalOwner)
               || string.IsNullOrWhiteSpace(submission.RollbackPlan.RollbackOwner)
               || string.IsNullOrWhiteSpace(submission.RollbackPlan.EmergencyOwner)
               || string.IsNullOrWhiteSpace(submission.MaterialIntake.BusinessScope)
               || string.IsNullOrWhiteSpace(submission.MaterialIntake.Department)
               || string.IsNullOrWhiteSpace(submission.MaterialIntake.PilotOwner)
               || submission.MaterialIntake.ExecutionWindowStart is null
               || submission.MaterialIntake.ExecutionWindowEnd is null
               || submission.MaterialIntake.RollbackWindowStart is null
               || submission.MaterialIntake.RollbackWindowEnd is null
               || string.IsNullOrWhiteSpace(submission.MaterialIntake.CredentialOwner)
               || string.IsNullOrWhiteSpace(submission.MaterialIntake.SecretStorageMode)
               || string.IsNullOrWhiteSpace(submission.MaterialIntake.SecretReferenceNameHash)
               || string.IsNullOrWhiteSpace(submission.MaterialIntake.PostRunAuditArchiveFormat)
               || string.IsNullOrWhiteSpace(submission.MaterialIntake.SignedApprovalRef);
    }
}

internal static class PilotAuthorizationSafeText
{
    private static readonly Regex SensitiveTextPattern = new(
        @"\b(?:authorization|proxy-authorization)\s*:\s*Bearer\s+[A-Za-z0-9._~+/=-]+|\b(token|bearer|x-api-key|api\s*key|apikey|connection\s*string|client[_-]?secret|access[_-]?token|refresh[_-]?token|raw\s*payload|raw\s*(business\s*)?(rows|records)|full\s*sql|free\s*sql|private\s*key)\b\s*[:=]?\s*[\w\-./+=:;,@]*|\b(?:openai|azure_openai|anthropic|cohere|gemini)_?api_?key\s*[:=]\s*[A-Za-z0-9._~+/=-]+|\b(sk|pk|rk)-[A-Za-z0-9][A-Za-z0-9_\-]{7,}\b|\b(password|pwd|secret)\s*=\s*[^;\s]+|\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b|https?://[^\s]+|\b(jdbc|odbc):[^\s]+|\b(postgres|postgresql|mysql|sqlserver|mongodb)://[^\s]+|密钥|令牌|访问令牌|刷新令牌|凭据|连接串|连接字符串|数据库连接|明文密码|原始载荷|原始行|原始业务行|完整\s*SQL|自由\s*SQL|私钥|密码",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? Redact(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? value
            : SensitiveTextPattern.Replace(value, "[redacted]");
    }
}
