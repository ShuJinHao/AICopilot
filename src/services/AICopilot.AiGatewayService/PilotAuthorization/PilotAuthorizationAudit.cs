using AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.PilotAuthorization;

internal static class PilotAuthorizationAudit
{
    private static readonly HashSet<string> SafeMetadataKeys = new(StringComparer.Ordinal)
    {
        "pilotAuthorizationStatus",
        "endpointCount",
        "maxRows",
        "timeRangeDays",
        "ownerCount",
        "machineValidationStatus"
    };

    private static readonly HashSet<string> SafeChangedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "security",
        "status"
    };

    public static Task WriteRejectedDraftAsync(
        IAuditLogWriter auditLogWriter,
        string actionCode,
        string summary,
        PilotAuthorizationSubmission? submission,
        CancellationToken cancellationToken)
    {
        return auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                actionCode,
                "PilotAuthorizationSubmission",
                submission?.Id.Value.ToString(),
                "PilotAuthorizationSubmission",
                AuditResults.Rejected,
                summary,
                ["security"],
                BuildMetadata(submission)),
            cancellationToken);
    }

    public static PilotAuthorizationAuditTimelineItemDto MapTimelineItem(
        Guid submissionId,
        AuditLogSummaryDto item)
    {
        return new PilotAuthorizationAuditTimelineItemDto(
            item.Id,
            submissionId,
            item.ActionCode,
            item.TargetType,
            item.Result,
            PilotAuthorizationSafeText.Redact(item.Summary) ?? string.Empty,
            item.ChangedFields
                .Where(field => SafeChangedFields.Contains(field))
                .OrderBy(field => field, StringComparer.Ordinal)
                .ToArray(),
            SanitizeTimelineMetadata(item.Metadata),
            item.CreatedAt);
    }

    public static Task WriteAsync(
        IAuditLogWriter auditLogWriter,
        string actionCode,
        string result,
        PilotAuthorizationSubmission submission,
        string summary,
        CancellationToken cancellationToken)
    {
        return auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.AiGateway,
                actionCode,
                "PilotAuthorizationSubmission",
                submission.Id.Value.ToString(),
                "PilotAuthorizationSubmission",
                result,
                summary,
                ["status"],
                new Dictionary<string, string>
                {
                    ["pilotAuthorizationStatus"] = submission.Status.ToString(),
                    ["endpointCount"] = submission.EndpointCodes.Length.ToString(),
                    ["maxRows"] = submission.MaxRows.ToString(),
                    ["timeRangeDays"] = submission.TimeRangeDays.ToString(),
                    ["ownerCount"] = "5",
                    ["machineValidationStatus"] = submission.MachineValidationStatus
                }),
            cancellationToken);
    }

    private static Dictionary<string, string>? BuildMetadata(PilotAuthorizationSubmission? submission)
    {
        return submission is null
            ? null
            : new Dictionary<string, string>
            {
                ["pilotAuthorizationStatus"] = submission.Status.ToString(),
                ["endpointCount"] = submission.EndpointCodes.Length.ToString(),
                ["maxRows"] = submission.MaxRows.ToString(),
                ["timeRangeDays"] = submission.TimeRangeDays.ToString(),
                ["ownerCount"] = "5",
                ["machineValidationStatus"] = submission.MachineValidationStatus
            };
    }

    private static IReadOnlyDictionary<string, string> SanitizeTimelineMetadata(
        IReadOnlyDictionary<string, string> metadata)
    {
        return metadata
            .Where(item => SafeMetadataKeys.Contains(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(
                item => item.Key,
                item => PilotAuthorizationSafeText.Redact(item.Value.Trim()) ?? string.Empty,
                StringComparer.Ordinal);
    }
}
