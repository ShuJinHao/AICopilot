using System.Text.RegularExpressions;
using AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;

namespace AICopilot.AiGatewayService.PilotAuthorization;

public sealed class PilotAuthorizationMachineValidator
{
    private static readonly HashSet<string> AllowedEndpoints = new(StringComparer.OrdinalIgnoreCase)
    {
        "devices",
        "capacity_summary",
        "device_logs",
        "pass_station_records"
    };

    private static readonly (Regex Pattern, string Reason)[] ForbiddenPatterns =
    [
        (new Regex(@"\b(token|bearer|x-api-key|api\s*key|apikey|client[_-]?secret|access[_-]?token|refresh[_-]?token|connection\s*string|raw\s*payload|raw\s*rows|full\s*sql|free\s*sql)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Sensitive or unrestricted execution wording is not allowed."),
        (new Regex(@"\b(jdbc|odbc):[^\s]+|\b(postgres|postgresql|mysql|sqlserver|mongodb)://[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Database URL material is not allowed."),
        (new Regex(@"\b(recipe|version)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Recipe/version scope is not allowed."),
        (new Regex(@"\b(cloud\s*write|insert\s+into|update\s+\w+|delete\s+from|truncate\s+table|drop\s+table)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Cloud write or mutating SQL wording is not allowed.")
    ];

    public PilotAuthorizationMachineValidationResult Validate(PilotAuthorizationSubmission submission)
    {
        var rejected = new List<string>();

        if (submission.EndpointCodes.Length == 0)
        {
            rejected.Add("At least one endpoint is required.");
        }

        rejected.AddRange(submission.EndpointCodes
            .Where(endpoint => !AllowedEndpoints.Contains(endpoint))
            .Select(endpoint => $"Endpoint is not allowed: {endpoint}."));

        if (submission.MaxRows is <= 0 or > 50)
        {
            rejected.Add("maxRows must be between 1 and 50.");
        }

        if (submission.TimeRangeDays is <= 0 or > 7)
        {
            rejected.Add("timeRangeDays must be between 1 and 7.");
        }

        foreach (var (label, value) in new[]
                 {
                     ("data owner", submission.DataOwner),
                     ("tool owner", submission.ToolOwner),
                     ("final owner", submission.FinalOwner),
                     ("rollback owner", submission.RollbackPlan.RollbackOwner),
                     ("emergency owner", submission.RollbackPlan.EmergencyOwner),
                     ("business scope", submission.MaterialIntake.BusinessScope),
                     ("department", submission.MaterialIntake.Department),
                     ("pilot owner", submission.MaterialIntake.PilotOwner),
                     ("credential owner", submission.MaterialIntake.CredentialOwner),
                     ("secret storage mode", submission.MaterialIntake.SecretStorageMode),
                     ("secret reference name hash", submission.MaterialIntake.SecretReferenceNameHash),
                     ("post-run audit archive format", submission.MaterialIntake.PostRunAuditArchiveFormat),
                     ("signed approval ref", submission.MaterialIntake.SignedApprovalRef)
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                rejected.Add($"{label} is required.");
            }
        }

        if (submission.MaterialIntake.ExecutionWindowStart is null
            || submission.MaterialIntake.ExecutionWindowEnd is null)
        {
            rejected.Add("execution window start and end are required.");
        }
        else if (submission.MaterialIntake.ExecutionWindowEnd <= submission.MaterialIntake.ExecutionWindowStart)
        {
            rejected.Add("execution window end must be after start.");
        }

        if (submission.MaterialIntake.RollbackWindowStart is null
            || submission.MaterialIntake.RollbackWindowEnd is null)
        {
            rejected.Add("rollback window start and end are required.");
        }
        else if (submission.MaterialIntake.RollbackWindowEnd <= submission.MaterialIntake.RollbackWindowStart)
        {
            rejected.Add("rollback window end must be after start.");
        }

        if (submission.ExpiresAt is null)
        {
            rejected.Add("expiresAt is required.");
        }
        else if (submission.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            rejected.Add("expiresAt must be in the future.");
        }

        var text = string.Join(
            "\n",
            submission.Title,
            submission.BusinessPurpose,
            string.Join(",", submission.EndpointCodes),
            submission.DataOwner,
            submission.ToolOwner,
            submission.FinalOwner,
            submission.RollbackPlan.RollbackOwner,
            submission.RollbackPlan.EmergencyOwner,
            submission.RollbackPlan.RollbackSummary,
            submission.EvidenceArchive.EvidenceSummary,
            submission.MaterialIntake.BusinessScope,
            submission.MaterialIntake.Department,
            submission.MaterialIntake.PilotOwner,
            submission.MaterialIntake.CredentialOwner,
            submission.MaterialIntake.SecretStorageMode,
            submission.MaterialIntake.SecretReferenceNameHash,
            submission.MaterialIntake.PostRunAuditArchiveFormat,
            submission.MaterialIntake.SignedApprovalRef);

        rejected.AddRange(ForbiddenPatterns
            .Where(rule => rule.Pattern.IsMatch(text))
            .Select(rule => rule.Reason));

        var sensitiveCheck = PilotAuthorizationSensitiveContentGuard.CheckSubmission(submission);
        if (!sensitiveCheck.IsSafe)
        {
            rejected.Add("Pilot authorization material contains sensitive or unrestricted content.");
        }

        var distinct = rejected
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return distinct.Length == 0
            ? PilotAuthorizationMachineValidationResult.Accepted()
            : PilotAuthorizationMachineValidationResult.Rejected(distinct);
    }
}
