namespace AICopilot.EntityFrameworkCore.AuditLogs;

internal static class AuditMetadataCodec
{
    private const string Prefix = "metadata:";
    private const int MaxMetadataValueLength = 256;

    private static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
    {
        "identityProvider",
        "cloudIssuer",
        "cloudTenantId",
        "cloudUserId",
        "cloudEmployeeNo",
        "cloudStatusVersion",
        "authMethod",
        "rejectionReason",
        "taskId",
        "taskCode",
        "workspaceCode",
        "stepOrder",
        "toolName",
        "artifactId",
        "artifactStatus",
        "artifactCount",
        "failureReason",
        "riskLevel",
        "pendingApprovalCount",
        "approvalType",
        "targetId",
        "approvalStatus",
        "pilotAuthorizationStatus",
        "endpointCount",
        "maxRows",
        "timeRangeDays",
        "ownerCount",
        "machineValidationStatus"
    };

    public static string[] Combine(
        IReadOnlyCollection<string>? changedFields,
        IReadOnlyDictionary<string, string>? metadata)
    {
        var values = new List<string>();

        if (changedFields is not null)
        {
            values.AddRange(changedFields
                .Where(field => !string.IsNullOrWhiteSpace(field))
                .Select(field => field.Trim())
                .Where(field => !field.StartsWith(Prefix, StringComparison.Ordinal)));
        }

        if (metadata is not null)
        {
            values.AddRange(metadata
                .Where(item => AllowedKeys.Contains(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => $"{Prefix}{item.Key}={Truncate(item.Value.Trim(), MaxMetadataValueLength)}"));
        }

        return values
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyCollection<string> ExtractChangedFields(IReadOnlyCollection<string> storedValues)
    {
        return storedValues
            .Where(value => !value.StartsWith(Prefix, StringComparison.Ordinal))
            .ToArray();
    }

    public static IReadOnlyDictionary<string, string> ExtractMetadata(IReadOnlyCollection<string> storedValues)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var value in storedValues.Where(value => value.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            var raw = value[Prefix.Length..];
            var separatorIndex = raw.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = raw[..separatorIndex];
            if (!AllowedKeys.Contains(key))
            {
                continue;
            }

            metadata[key] = raw[(separatorIndex + 1)..];
        }

        return metadata;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
