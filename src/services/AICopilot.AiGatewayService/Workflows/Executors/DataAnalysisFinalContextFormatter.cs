using System.Collections;
using System.Globalization;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.Services.CrossCutting.Serialization;
using AICopilot.Visualization;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public static class DataAnalysisFinalContextFormatter
{
    private const int PreviewRowLimit = 3;
    private const int MaxStringValueLength = 240;
    private const string RedactedValue = "[已移除疑似指令或内部细节]";

    private static readonly string[] InternalFieldNames =
    [
        "sql",
        "sqlText",
        "query",
        "database",
        "databaseName",
        "dbName",
        "sourceName",
        "effectiveSourceName",
        "tableName",
        "viewName",
        "connectionString",
        "connection",
        "host",
        "server",
        "password",
        "pwd",
        "userId",
        "username"
    ];

    private static readonly string[] InternalFieldFragments =
    [
        "connection",
        "connectionstring",
        "password",
        "tablename",
        "viewname",
        "sourcename",
        "effectivesourcename",
        "databasename"
    ];

    private static readonly string[] DangerousTextFragments =
    [
        "执行 SQL",
        "执行SQL",
        "泄露表名",
        "调用写工具",
        "绕过审批",
        "跳过审批",
        "忽略系统规则",
        "忽略安全规则",
        "ignore previous",
        "ignore system",
        "ignore instructions",
        "connection string",
        "connectionstring",
        "password=",
        "pwd=",
        "user id=",
        "data source=",
        "server=",
        "host=",
        "database="
    ];

    public static string FormatSemantic(
        AnalysisDto analysis,
        SemanticSummaryDto semanticSummary,
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        bool isTruncated)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(semanticSummary);

        var context = new
        {
            analysis = BuildSafeAnalysis(analysis, trustSourceLabel: true),
            semantic_summary = BuildSafeSemanticSummary(semanticSummary),
            business_data_preview = BuildBusinessDataPreview(rows, analysis.Metadata),
            query_scope = SanitizeTextValue(semanticSummary.Scope) ?? "结果上限以内的匹配记录",
            is_truncated = isTruncated
        };

        return context.ToJson();
    }

    public static string FormatFreeForm(
        AnalysisDto? analysis,
        VisualDecisionDto? decision,
        IEnumerable<dynamic>? rows,
        IEnumerable<SchemaColumn>? schema)
    {
        var context = new
        {
            analysis = BuildSafeAnalysis(analysis, trustSourceLabel: false),
            visual_decision = BuildSafeVisualDecision(decision),
            business_data_preview = BuildBusinessDataPreview(rows, analysis?.Metadata, schema),
            query_scope = "基于当前只读数据分析结果的业务预览。"
        };

        return context.ToJson();
    }

    private static object? BuildSafeAnalysis(AnalysisDto? analysis, bool trustSourceLabel)
    {
        if (analysis is null)
        {
            return null;
        }

        var sourceLabel = trustSourceLabel
            ? SanitizeTextValue(analysis.SourceLabel)
            : "只读业务数据源";

        return new
        {
            source_label = string.IsNullOrWhiteSpace(sourceLabel) ? "只读业务数据源" : sourceLabel,
            description = SanitizeTextValue(analysis.Description),
            metadata = analysis.Metadata
                .Where(item => !IsInternalFieldName(item.Name))
                .Select(item => new
                {
                    name = ResolveBusinessFieldName(item.Name, analysis.Metadata),
                    description = SanitizeTextValue(item.Description)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.name))
                .ToList()
        };
    }

    private static object BuildSafeSemanticSummary(SemanticSummaryDto semanticSummary)
    {
        return new
        {
            target = SanitizeTextValue(semanticSummary.Target),
            conclusion = SanitizeTextValue(semanticSummary.Conclusion),
            metrics = semanticSummary.Metrics.Select(metric => new
            {
                name = SanitizeTextValue(metric.Label) ?? SanitizeTextValue(metric.Name),
                value = SanitizeTextValue(metric.Value)
            }).ToList(),
            highlights = semanticSummary.Highlights
                .Select(SanitizeTextValue)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList(),
            scope = SanitizeTextValue(semanticSummary.Scope)
        };
    }

    private static object? BuildSafeVisualDecision(VisualDecisionDto? decision)
    {
        if (decision is null)
        {
            return null;
        }

        return new
        {
            type = decision.Type.ToString(),
            title = SanitizeTextValue(decision.Title),
            description = SanitizeTextValue(decision.Description),
            chart = decision.ChartConfig is null
                ? null
                : new
                {
                    category = decision.ChartConfig.Category.ToString()
                },
            unit = SanitizeTextValue(decision.Unit)
        };
    }

    private static List<Dictionary<string, object?>> BuildBusinessDataPreview(
        IEnumerable? rows,
        IReadOnlyCollection<MetadataItemDto>? metadata,
        IEnumerable<SchemaColumn>? schema = null)
    {
        var preview = new List<Dictionary<string, object?>>();
        if (rows is null)
        {
            return preview;
        }

        var schemaDescriptions = schema?
            .Where(column => !IsInternalFieldName(column.Name))
            .ToDictionary(column => column.Name, column => column.Name, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Cast<object?>())
        {
            if (row is null || !TryGetRowValues(row, out var values))
            {
                continue;
            }

            var safeRow = new Dictionary<string, object?>();
            foreach (var (fieldName, value) in values)
            {
                if (IsInternalFieldName(fieldName))
                {
                    continue;
                }

                var label = ResolveBusinessFieldName(fieldName, metadata, schemaDescriptions);
                if (string.IsNullOrWhiteSpace(label) || IsInternalFieldName(label))
                {
                    continue;
                }

                var outputLabel = DeduplicateLabel(safeRow, label);
                safeRow[outputLabel] = SanitizeValue(value);
            }

            if (safeRow.Count > 0)
            {
                preview.Add(safeRow);
            }

            if (preview.Count >= PreviewRowLimit)
            {
                break;
            }
        }

        return preview;
    }

    private static bool TryGetRowValues(
        object row,
        out IEnumerable<KeyValuePair<string, object?>> values)
    {
        switch (row)
        {
            case IReadOnlyDictionary<string, object?> readOnlyDictionary:
                values = readOnlyDictionary;
                return true;

            case IDictionary<string, object?> dictionary:
                values = dictionary;
                return true;

            default:
                values = [];
                return false;
        }
    }

    private static string ResolveBusinessFieldName(
        string fieldName,
        IReadOnlyCollection<MetadataItemDto>? metadata,
        IReadOnlyDictionary<string, string>? schemaDescriptions = null)
    {
        var metadataDescription = metadata?
            .FirstOrDefault(item => string.Equals(item.Name, fieldName, StringComparison.OrdinalIgnoreCase))
            ?.Description;

        if (!string.IsNullOrWhiteSpace(metadataDescription) && !IsInternalFieldName(metadataDescription))
        {
            return metadataDescription.Trim();
        }

        if (schemaDescriptions?.TryGetValue(fieldName, out var schemaDescription) == true
            && !string.IsNullOrWhiteSpace(schemaDescription)
            && !IsInternalFieldName(schemaDescription))
        {
            return schemaDescription.Trim();
        }

        return IsInternalFieldName(fieldName) ? string.Empty : fieldName;
    }

    private static string DeduplicateLabel(Dictionary<string, object?> row, string label)
    {
        if (!row.ContainsKey(label))
        {
            return label;
        }

        var index = 2;
        while (row.ContainsKey($"{label}_{index}"))
        {
            index++;
        }

        return $"{label}_{index}";
    }

    private static object? SanitizeValue(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        return value switch
        {
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
            _ => SanitizeTextValue(value.ToString())
        };
    }

    private static string? SanitizeTextValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var trimmed = value.Trim();
        if (LooksLikeInstructionOrInternalDetail(trimmed))
        {
            return RedactedValue;
        }

        return trimmed.Length <= MaxStringValueLength
            ? trimmed
            : trimmed[..MaxStringValueLength] + "...";
    }

    private static bool LooksLikeInstructionOrInternalDetail(string value)
    {
        foreach (var fragment in DangerousTextFragments)
        {
            if (value.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var lower = value.ToLowerInvariant();
        return (lower.Contains("select ", StringComparison.Ordinal) && lower.Contains(" from ", StringComparison.Ordinal))
               || lower.StartsWith("insert ", StringComparison.Ordinal)
               || lower.StartsWith("update ", StringComparison.Ordinal)
               || lower.StartsWith("delete ", StringComparison.Ordinal)
               || lower.StartsWith("drop ", StringComparison.Ordinal)
               || lower.StartsWith("alter ", StringComparison.Ordinal)
               || lower.StartsWith("truncate ", StringComparison.Ordinal)
               || lower.StartsWith("exec ", StringComparison.Ordinal)
               || lower.StartsWith("execute ", StringComparison.Ordinal);
    }

    private static bool IsInternalFieldName(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return true;
        }

        var normalized = fieldName.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (InternalFieldNames.Any(field => string.Equals(
                normalized,
                field.Replace("_", string.Empty, StringComparison.Ordinal),
                StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return InternalFieldFragments.Any(fragment =>
            normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}
