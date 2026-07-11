using System.Collections;
using System.Globalization;
using System.Text.Json;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.Services.CrossCutting.Serialization;
using AICopilot.Visualization;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public static class DataAnalysisFinalContextFormatter
{
    private const int PreviewRowLimit = 3;
    private const int MaxBusinessFieldLabelLength = 80;
    private const int MaxStringValueLength = 240;
    private const string RedactedValue = "[已移除疑似指令或内部细节]";
    private const string SafeBusinessFieldLabel = "业务字段";

    private static readonly string[] InternalFieldNames =
    [
        "sql",
        "sqlText",
        "query",
        "database",
        "dbName",
        "host",
        "server",
        "pwd",
        "userId",
        "username"
    ];

    private static readonly string[] InternalFieldFragments =
    [
        "connection",
        "tablename",
        "viewname",
        "sourcename",
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
        bool isTruncated,
        SemanticQueryPlan? plan = null,
        int? returnedRowCount = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(semanticSummary);

        var materializedRows = MaterializeRows(rows);
        var previewRows = materializedRows
            .Take(PreviewRowLimit)
            .Select(row => (IReadOnlyDictionary<string, object?>)row)
            .ToList();
        var fieldLabels = BuildBusinessFieldLabelMap(analysis.Metadata, schema: null, previewRows);
        var context = new
        {
            analysis = BuildSafeAnalysis(analysis, trustSourceLabel: true, fieldLabels),
            source_mode = BuildSourceMode(analysis.SourceLabel),
            answer_contract = BuildAnswerContract(),
            query_execution = BuildQueryExecution(plan, materializedRows, returnedRowCount),
            semantic_summary = BuildSafeSemanticSummary(semanticSummary),
            display_blocks = DeviceLogSemanticDisplayBuilder.BuildContextBlocks(
                plan,
                semanticSummary,
                materializedRows),
            business_data_preview = BuildBusinessDataPreview(previewRows, fieldLabels),
            query_scope = SanitizeTextValue(semanticSummary.Scope) ?? "结果上限以内的匹配记录",
            is_truncated = isTruncated
        };

        return context.ToJson();
    }

    public static string CompactForFinalPrompt(string context)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return context;
        }

        try
        {
            using var document = JsonDocument.Parse(context);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return TruncateContextForFinalPrompt(context);
            }

            var compact = new Dictionary<string, object?>(StringComparer.Ordinal);
            AddJsonProperty(compact, document.RootElement, "analysis");
            AddJsonProperty(compact, document.RootElement, "source_mode");
            AddJsonProperty(compact, document.RootElement, "query_execution");
            AddJsonProperty(compact, document.RootElement, "semantic_summary");
            AddCompactDisplayBlocks(compact, document.RootElement);
            AddJsonProperty(compact, document.RootElement, "query_scope");
            AddJsonProperty(compact, document.RootElement, "is_truncated");
            compact["compact_note"] = "上下文已为 token 预算压缩；重复协议与预览已省略，完整结构化组件已通过流式 Widget 输出。";

            return compact.ToJson();
        }
        catch (JsonException)
        {
            return TruncateContextForFinalPrompt(context);
        }
    }

    public static string FormatFreeForm(
        AnalysisDto? analysis,
        VisualDecisionDto? decision,
        IEnumerable<dynamic>? rows,
        IEnumerable<SchemaColumn>? schema)
    {
        var previewRows = MaterializePreviewRows(rows);
        var fieldLabels = BuildBusinessFieldLabelMap(analysis?.Metadata, schema, previewRows);
        var context = new
        {
            analysis = BuildSafeAnalysis(analysis, trustSourceLabel: false, fieldLabels),
            source_mode = "DataAnalysis/Text-to-SQL 补充分析",
            answer_contract = BuildAnswerContract(),
            visual_decision = BuildSafeVisualDecision(decision),
            business_data_preview = BuildBusinessDataPreview(previewRows, fieldLabels),
            query_scope = "基于当前只读数据分析结果的业务预览。"
        };

        return context.ToJson();
    }

    private static string BuildSourceMode(string? sourceLabel)
    {
        if (!string.IsNullOrWhiteSpace(sourceLabel) &&
            sourceLabel.Contains("Cloud AiRead API", StringComparison.OrdinalIgnoreCase))
        {
            return "Cloud 已有正式只读数据";
        }

        if (!string.IsNullOrWhiteSpace(sourceLabel) &&
            sourceLabel.Contains("DataAnalysis", StringComparison.OrdinalIgnoreCase))
        {
            return "DataAnalysis/Text-to-SQL 补充分析";
        }

        return "只读业务数据源";
    }

    private static object BuildAnswerContract()
    {
        return new
        {
            must_distinguish = new[]
            {
                "Cloud 已有数据",
                "AI 推断分析",
                "建议动作",
                "不能直接执行的动作"
            },
            cloud_write_boundary = "AICopilot 不直接修改、提交、下发、补录、删除或上传 Cloud 业务数据。",
            evidence_boundary = "只能回答本轮已查询范围；未覆盖范围不得推断。",
            follow_up_rule = "追问新范围必须看本轮 query_execution。",
            default_sections = new[]
            {
                "结论",
                "关键指标",
                "关键记录",
                "查询范围"
            },
            device_log_display_sections = new[]
            {
                "结论",
                "关键指标",
                "关键记录",
                "可能原因",
                "建议动作",
                "不能直接执行的动作",
                "查询范围"
            }
        };
    }

    private static object BuildQueryExecution(
        SemanticQueryPlan? plan,
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        int? returnedRowCount)
    {
        var rowCount = returnedRowCount ?? TryCountRows(rows);
        return new
        {
            executed = plan is not null,
            target = plan?.Target.ToString(),
            kind = plan?.Kind.ToString(),
            filters = plan?.Filters.Select(filter => new
            {
                field = SanitizeTextValue(filter.Field),
                @operator = filter.Operator.ToString(),
                value = SanitizeTextValue(filter.Value)
            }).ToList() ?? [],
            time_range = plan?.TimeRange is null
                ? null
                : new
                {
                    field = SanitizeTextValue(plan.TimeRange.Field),
                    start = plan.TimeRange.Start?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    end = plan.TimeRange.End?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                },
            limit = plan?.Limit,
            returned_row_count = rowCount
        };
    }

    private static int? TryCountRows(IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        return rows switch
        {
            IReadOnlyCollection<IReadOnlyDictionary<string, object?>> readOnlyCollection => readOnlyCollection.Count,
            ICollection<IReadOnlyDictionary<string, object?>> collection => collection.Count,
            _ => null
        };
    }

    private static void AddJsonProperty(Dictionary<string, object?> target, JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var property))
        {
            target[propertyName] = property.Clone();
        }
    }

    private static void AddCompactDisplayBlocks(Dictionary<string, object?> target, JsonElement root)
    {
        if (!root.TryGetProperty("display_blocks", out var blocksElement)
            || blocksElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var compactBlocks = new List<Dictionary<string, object?>>();
        foreach (var block in blocksElement.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var blockId = GetJsonStringProperty(block, "id");
            if (string.Equals(blockId, "device_log_metrics", StringComparison.OrdinalIgnoreCase))
            {
                compactBlocks.Add(BuildJsonObject(block, "type", "id", "title", "metrics"));
            }
            else if (string.Equals(blockId, "device_log_evidence_table", StringComparison.OrdinalIgnoreCase))
            {
                compactBlocks.Add(BuildJsonObject(
                    block,
                    "type",
                    "id",
                    "title",
                    "rows",
                    "displayed_row_count",
                    "source_row_count",
                    "limit_note"));
            }
        }

        if (compactBlocks.Count > 0)
        {
            target["display_blocks"] = compactBlocks;
        }
    }

    private static Dictionary<string, object?> BuildJsonObject(JsonElement element, params string[] propertyNames)
    {
        var output = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var property))
            {
                output[propertyName] = property.Clone();
            }
        }

        return output;
    }

    private static string? GetJsonStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string TruncateContextForFinalPrompt(string context)
    {
        const int maxContextLength = 2000;
        return context.Length <= maxContextLength
            ? context
            : context[..maxContextLength] + "...";
    }

    private static IReadOnlyList<Dictionary<string, object?>> MaterializeRows(
        IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        return rows switch
        {
            IReadOnlyList<Dictionary<string, object?>> dictionaryList => dictionaryList,
            _ => rows.Select(row => new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase)).ToList()
        };
    }

    private static object? BuildSafeAnalysis(
        AnalysisDto? analysis,
        bool trustSourceLabel,
        IReadOnlyDictionary<string, string> fieldLabels)
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
            metadata = BuildSafeMetadata(analysis.Metadata, fieldLabels)
        };
    }

    private static List<Dictionary<string, string>> BuildSafeMetadata(
        IEnumerable<MetadataItemDto> metadata,
        IReadOnlyDictionary<string, string> fieldLabels)
    {
        var safeMetadata = new List<Dictionary<string, string>>();
        var emittedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in metadata)
        {
            if (!emittedFields.Add(item.Name)
                || !fieldLabels.TryGetValue(item.Name, out var label))
            {
                continue;
            }

            safeMetadata.Add(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = label,
                ["description"] = label
            });
        }

        return safeMetadata;
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

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> MaterializePreviewRows(IEnumerable? rows)
    {
        var previewRows = new List<IReadOnlyDictionary<string, object?>>(PreviewRowLimit);
        if (rows is null)
        {
            return previewRows;
        }

        foreach (var row in rows.Cast<object?>())
        {
            if (row is null || !TryGetRowValues(row, out var values))
            {
                continue;
            }

            var materializedRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (fieldName, value) in values)
            {
                materializedRow.TryAdd(fieldName, value);
            }

            previewRows.Add(materializedRow);

            if (previewRows.Count >= PreviewRowLimit)
            {
                break;
            }
        }

        return previewRows;
    }

    private static IReadOnlyDictionary<string, string> BuildBusinessFieldLabelMap(
        IReadOnlyCollection<MetadataItemDto>? metadata,
        IEnumerable<SchemaColumn>? schema,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> previewRows)
    {
        var orderedFieldNames = new List<string>();
        var seenFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var metadataDescriptions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        void AddFieldName(string? fieldName)
        {
            if (!IsInternalFieldName(fieldName) && seenFieldNames.Add(fieldName!))
            {
                orderedFieldNames.Add(fieldName!);
            }
        }

        if (metadata is not null)
        {
            foreach (var item in metadata)
            {
                if (IsInternalFieldName(item.Name))
                {
                    continue;
                }

                AddFieldName(item.Name);
                metadataDescriptions.TryAdd(item.Name, item.Description);
            }
        }

        if (schema is not null)
        {
            foreach (var column in schema)
            {
                if (IsInternalFieldName(column.Name))
                {
                    continue;
                }

                AddFieldName(column.Name);
            }
        }

        foreach (var row in previewRows)
        {
            foreach (var fieldName in row.Keys)
            {
                AddFieldName(fieldName);
            }
        }

        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fieldName in orderedFieldNames)
        {
            metadataDescriptions.TryGetValue(fieldName, out var metadataDescription);
            var baseLabel = NormalizeBusinessFieldLabelCandidate(metadataDescription)
                            ?? NormalizeBusinessFieldLabelCandidate(fieldName)
                            ?? SafeBusinessFieldLabel;
            labels[fieldName] = AllocateUniqueBusinessFieldLabel(baseLabel, usedLabels);
        }

        return labels;
    }

    private static string? NormalizeBusinessFieldLabelCandidate(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)
            || ContainsDisallowedLabelCharacter(candidate))
        {
            return null;
        }

        var trimmed = candidate.Trim();
        if (trimmed.Length > MaxBusinessFieldLabelLength
            || IsInternalFieldName(trimmed))
        {
            return null;
        }

        var sanitized = SanitizeTextValue(trimmed);
        return string.IsNullOrWhiteSpace(sanitized)
               || string.Equals(sanitized, RedactedValue, StringComparison.Ordinal)
            ? null
            : sanitized;
    }

    private static bool ContainsDisallowedLabelCharacter(string value)
    {
        return value.Any(character => char.IsControl(character)
                                      || char.GetUnicodeCategory(character) is UnicodeCategory.LineSeparator
                                          or UnicodeCategory.ParagraphSeparator);
    }

    private static string AllocateUniqueBusinessFieldLabel(string baseLabel, HashSet<string> usedLabels)
    {
        if (usedLabels.Add(baseLabel))
        {
            return baseLabel;
        }

        for (var index = 2; ; index++)
        {
            var suffix = $"_{index}";
            var prefixLength = Math.Min(baseLabel.Length, MaxBusinessFieldLabelLength - suffix.Length);
            var candidate = baseLabel[..prefixLength] + suffix;
            if (usedLabels.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static List<Dictionary<string, object?>> BuildBusinessDataPreview(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyDictionary<string, string> fieldLabels)
    {
        var preview = new List<Dictionary<string, object?>>(rows.Count);
        foreach (var row in rows)
        {
            var safeRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (fieldName, value) in row)
            {
                if (fieldLabels.TryGetValue(fieldName, out var label))
                {
                    safeRow[label] = SanitizeValue(value);
                }
            }

            if (safeRow.Count > 0)
            {
                preview.Add(safeRow);
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

    private static object? SanitizeValue(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        if (value is JsonDocument document)
        {
            value = document.RootElement;
        }

        if (value is JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Undefined:
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return element.GetBoolean();
                case JsonValueKind.Number when element.TryGetInt64(out var integer):
                    return integer;
                case JsonValueKind.Number when element.TryGetDecimal(out var decimalValue):
                    return decimalValue;
                case JsonValueKind.Number when element.TryGetDouble(out var doubleValue):
                    return doubleValue;
                case JsonValueKind.String:
                    return SanitizeTextValue(element.GetString());
                case JsonValueKind.Object:
                case JsonValueKind.Array:
                default:
                    return RedactedValue;
            }
        }

        return value switch
        {
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
            string text => SanitizeTextValue(text),
            char character => character.ToString(),
            Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
            Enum enumValue => SanitizeTextValue(enumValue.ToString()),
            _ => RedactedValue
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

        if (CloudReadOnlyGovernedSchema.BlockedFieldFragments.Any(fragment =>
                normalized.Contains(
                    fragment.Replace("_", string.Empty, StringComparison.Ordinal)
                        .Replace("-", string.Empty, StringComparison.Ordinal),
                    StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return InternalFieldFragments.Any(fragment =>
            normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}
