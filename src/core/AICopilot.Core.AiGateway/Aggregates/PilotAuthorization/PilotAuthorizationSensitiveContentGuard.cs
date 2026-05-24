using System.Text.RegularExpressions;

namespace AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;

public readonly record struct PilotAuthorizationSensitiveField(string Name, string? Value);

public sealed record PilotAuthorizationSensitiveContentCheck(bool IsSafe, IReadOnlyCollection<string> Reasons)
{
    public static PilotAuthorizationSensitiveContentCheck Safe() => new(true, []);

    public static PilotAuthorizationSensitiveContentCheck Unsafe(IReadOnlyCollection<string> reasons) =>
        new(false, reasons);
}

public sealed class PilotAuthorizationUnsafeContentException(string message) : InvalidOperationException(message);

public static class PilotAuthorizationSensitiveContentGuard
{
    private static readonly (Regex Pattern, string Reason)[] ForbiddenPatterns =
    [
        (new Regex(@"\bBearer\s+[A-Za-z0-9._~+/=-]{8,}", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Bearer token material is not allowed."),
        (new Regex(@"\b(sk|pk|rk)-[A-Za-z0-9][A-Za-z0-9_\-]{7,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "API key style secret material is not allowed."),
        (new Regex(@"\bapi\s*key\b|\bapikey\b|\btoken\b|\bsecret\b|\bpassword\b|\bpwd\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Secret, token, or credential wording is not allowed."),
        (new Regex(@"\b(connection\s*string|data\s+source|initial\s+catalog|user\s+id|uid|server\s*=|host\s*=|database\s*=)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Connection string material is not allowed."),
        (new Regex(@"\b(raw\s*payload|raw\s*(business\s*)?(rows|records))\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Raw payload or raw business rows are not allowed."),
        (new Regex(@"\b(full\s*sql|free\s*sql)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Full or free SQL wording is not allowed."),
        (new Regex(@"\b(select\s+.+\s+from|insert\s+into|update\s+\w+\s+set|delete\s+from|drop\s+table|truncate\s+table)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "SQL text is not allowed."),
        (new Regex(@"-----BEGIN\s+[^-]*PRIVATE\s+KEY-----", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Private key material is not allowed."),
        (new Regex(@"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b", RegexOptions.Compiled), "JWT material is not allowed."),
        (new Regex(@"\b(postgres|postgresql|mysql|sqlserver|mongodb)://[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Database URL material is not allowed."),
        (new Regex(@"https?://[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Real endpoint URL material is not allowed."),
        (new Regex(@"密钥|令牌|连接串|连接字符串|原始载荷|原始行|原始业务行|完整\s*SQL|自由\s*SQL|私钥|密码", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Sensitive Chinese security wording is not allowed.")
    ];

    public static PilotAuthorizationSensitiveContentCheck Check(IEnumerable<PilotAuthorizationSensitiveField> fields)
    {
        var reasons = new List<string>();
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Value))
            {
                continue;
            }

            reasons.AddRange(ForbiddenPatterns
                .Where(rule => rule.Pattern.IsMatch(field.Value))
                .Select(rule => rule.Reason));
        }

        var distinct = reasons
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(reason => reason, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return distinct.Length == 0
            ? PilotAuthorizationSensitiveContentCheck.Safe()
            : PilotAuthorizationSensitiveContentCheck.Unsafe(distinct);
    }

    public static PilotAuthorizationSensitiveContentCheck CheckSubmission(PilotAuthorizationSubmission submission)
    {
        return Check(BuildSubmissionFields(submission));
    }

    public static void ThrowIfUnsafe(IEnumerable<PilotAuthorizationSensitiveField> fields)
    {
        var check = Check(fields);
        if (!check.IsSafe)
        {
            throw new PilotAuthorizationUnsafeContentException(
                "Pilot authorization material contains sensitive or unrestricted content.");
        }
    }

    public static IEnumerable<PilotAuthorizationSensitiveField> BuildSubmissionFields(
        PilotAuthorizationSubmission submission)
    {
        return
        [
            new("title", submission.Title),
            new("businessPurpose", submission.BusinessPurpose),
            new("endpointCodes", string.Join("\n", submission.EndpointCodes)),
            new("dataOwner", submission.DataOwner),
            new("toolOwner", submission.ToolOwner),
            new("finalOwner", submission.FinalOwner),
            new("rollbackOwner", submission.RollbackPlan.RollbackOwner),
            new("emergencyOwner", submission.RollbackPlan.EmergencyOwner),
            new("rollbackSummary", submission.RollbackPlan.RollbackSummary),
            new("evidenceSummary", submission.EvidenceArchive.EvidenceSummary),
            new("credentialWindowPlanningSummary", submission.CredentialWindow.PlanningSummary),
            new("lastDecisionReason", submission.Review.LastDecisionReason),
            new("businessScope", submission.MaterialIntake.BusinessScope),
            new("department", submission.MaterialIntake.Department),
            new("pilotOwner", submission.MaterialIntake.PilotOwner),
            new("credentialOwner", submission.MaterialIntake.CredentialOwner),
            new("secretStorageMode", submission.MaterialIntake.SecretStorageMode),
            new("secretReferenceNameHash", submission.MaterialIntake.SecretReferenceNameHash),
            new("postRunAuditArchiveFormat", submission.MaterialIntake.PostRunAuditArchiveFormat),
            new("signedApprovalRef", submission.MaterialIntake.SignedApprovalRef)
        ];
    }
}
