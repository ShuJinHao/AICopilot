using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Services.Contracts;

namespace AICopilot.DataAnalysisService.BusinessDatabases;

internal static class BusinessQueryResultMapper
{
    public const string SimulationSourceLabel = "AI \u72ec\u7acb\u6a21\u62df\u4e1a\u52a1\u5e93";
    private const int MaxPreviewRows = 50;
    private const int MaxStringValueLength = 512;

    public static BusinessQueryResultDto Map(
        BusinessDatabase database,
        string sql,
        DatabaseQueryResult queryResult,
        BusinessQuerySafetySchema safetySchema,
        DataSourceSelectionMode selectionMode)
    {
        var sanitization = SanitizeRows(queryResult.Rows, safetySchema);
        var rows = sanitization.Rows;

        var columns = BuildColumns(rows);
        var sourceMode = BusinessDatabaseContractMapper.ToContractExternalSystemType(database.ExternalSystemType);
        var isSimulation = database.ExternalSystemType == BusinessDataExternalSystemType.SimulationBusiness;
        var warningCodes = new List<string> { "SANITIZED_PREVIEW" };
        if (queryResult.Rows.Count > rows.Count)
        {
            warningCodes.Add("BOUNDED_PREVIEW_APPLIED");
        }

        if (sanitization.RedactedColumnHashes.Count > 0 || sanitization.RedactedValueCount > 0)
        {
            warningCodes.Add("SENSITIVE_VALUE_REDACTED");
        }

        return new BusinessQueryResultDto(
            database.Id,
            database.Name,
            "BusinessDatabase",
            sourceMode,
            isSimulation,
            isSimulation ? SimulationSourceLabel : database.Name,
            ComputeQueryHash(sql),
            queryResult.ReturnedRowCount,
            queryResult.IsTruncated,
            columns,
            rows,
            DateTimeOffset.UtcNow,
            queryResult.ElapsedMilliseconds,
            new BusinessQueryGovernanceDto(
                IsSanitizedPreview: true,
                selectionMode,
                rows.Count,
                MaxPreviewRows,
                warningCodes,
                sanitization.RedactedColumnHashes,
                safetySchema.AllowedTables.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray()));
    }

    public static string ComputeQueryHash(string sql)
    {
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(sql ?? string.Empty)))
            .ToLowerInvariant();
    }

    private static SanitizedRows SanitizeRows(
        IReadOnlyList<Dictionary<string, object?>> rows,
        BusinessQuerySafetySchema safetySchema)
    {
        var blockedFragments = BusinessQuerySensitiveFieldCatalog.GetAll(safetySchema);
        var redactedColumnHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var redactedValueCount = 0;
        var columnMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sanitizedRows = rows
            .Take(MaxPreviewRows)
            .Select(row =>
            {
                var sanitized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                var columnIndex = 0;
                foreach (var item in row)
                {
                    var safeName = ResolveSafeColumnName(item.Key, columnIndex, blockedFragments, redactedColumnHashes, columnMap);
                    var value = SanitizeValue(item.Key, item.Value, blockedFragments, ref redactedValueCount);
                    sanitized[safeName] = value;
                    columnIndex++;
                }

                return (IReadOnlyDictionary<string, object?>)sanitized;
            })
            .ToArray();

        return new SanitizedRows(
            sanitizedRows,
            redactedColumnHashes.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray(),
            redactedValueCount);
    }

    private static string ResolveSafeColumnName(
        string columnName,
        int columnIndex,
        IReadOnlyCollection<string> blockedFragments,
        ISet<string> redactedColumnHashes,
        IDictionary<string, string> columnMap)
    {
        if (columnMap.TryGetValue(columnName, out var mapped))
        {
            return mapped;
        }

        if (!ContainsSensitiveFragment(columnName, blockedFragments))
        {
            columnMap[columnName] = columnName;
            return columnName;
        }

        redactedColumnHashes.Add(ComputeQueryHash(columnName));
        var safeName = $"redacted_column_{columnIndex + 1}";
        columnMap[columnName] = safeName;
        return safeName;
    }

    private static object? SanitizeValue(
        string columnName,
        object? value,
        IReadOnlyCollection<string> blockedFragments,
        ref int redactedValueCount)
    {
        if (value is null)
        {
            return null;
        }

        if (ContainsSensitiveFragment(columnName, blockedFragments))
        {
            redactedValueCount++;
            return "[redacted]";
        }

        if (value is string text)
        {
            if (ContainsSensitiveFragment(text, blockedFragments) ||
                Regex.IsMatch(text, @"(?i)(sk-[a-z0-9_\-]{8,}|password\s*=|token\s*=|api[_-]?key\s*=|connection\s*string|secret\s*=)"))
            {
                redactedValueCount++;
                return "[redacted]";
            }

            return text.Length <= MaxStringValueLength
                ? text
                : string.Concat(text.AsSpan(0, MaxStringValueLength), "...[truncated]");
        }

        var valueType = value.GetType();
        if (valueType.IsPrimitive ||
            value is decimal ||
            value is DateTime ||
            value is DateTimeOffset ||
            value is Guid)
        {
            return value;
        }

        redactedValueCount++;
        return "[redacted]";
    }

    private static bool ContainsSensitiveFragment(
        string value,
        IReadOnlyCollection<string> blockedFragments)
    {
        return blockedFragments.Any(fragment =>
            !string.IsNullOrWhiteSpace(fragment) &&
            value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<BusinessQueryColumnDto> BuildColumns(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var firstRow = rows.FirstOrDefault();
        if (firstRow is null)
        {
            return [];
        }

        return firstRow
            .Select(column => new BusinessQueryColumnDto(
                column.Key,
                column.Value?.GetType().Name ?? "Object"))
            .ToArray();
    }

    private sealed record SanitizedRows(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
        IReadOnlyList<string> RedactedColumnHashes,
        int RedactedValueCount);
}
