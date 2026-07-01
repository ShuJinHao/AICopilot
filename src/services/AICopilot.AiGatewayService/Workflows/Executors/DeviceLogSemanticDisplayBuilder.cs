using System.Globalization;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.Visualization.Widgets;

namespace AICopilot.AiGatewayService.Workflows.Executors;

internal static class DeviceLogSemanticDisplayBuilder
{
    private const int EvidenceRowLimit = 20;
    private const int MaxStringValueLength = 240;
    private const string RedactedValue = "[已移除疑似指令或内部细节]";

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

    private static readonly IssueCategoryRule[] IssueCategoryRules =
    [
        new("温度异常", ["temperature", "temp", "温度", "高温", "过温", "加热"]),
        new("电机/驱动", ["motor", "drive", "servo", "电机", "驱动", "伺服", "过载", "overload"]),
        new("通信/连接", ["communication", "connect", "disconnect", "offline", "timeout", "通讯", "通信", "连接", "断开", "离线", "超时"]),
        new("压力/气压", ["pressure", "air", "压力", "气压", "真空"]),
        new("传感器", ["sensor", "probe", "传感器", "探头", "检测"]),
        new("安全/门禁", ["safety", "door", "interlock", "安全", "门禁", "联锁", "急停"])
    ];

    public static IReadOnlyList<object> BuildContextBlocks(
        SemanticQueryPlan? plan,
        SemanticSummaryDto semanticSummary,
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(semanticSummary);
        ArgumentNullException.ThrowIfNull(rows);

        if (plan?.Target != SemanticQueryTarget.DeviceLog)
        {
            return [];
        }

        var metrics = BuildMetrics(rows);
        var levelDistribution = BuildLevelDistribution(rows);
        var timeDistribution = BuildTimeDistribution(rows);
        var issueCategories = BuildIssueCategories(rows);
        var evidenceRows = BuildEvidenceRows(rows);

        return
        [
            new
            {
                type = "MetricStripBlock",
                id = "device_log_metrics",
                title = "设备日志关键指标",
                metrics = metrics.Select(metric => new
                {
                    name = metric.Name,
                    label = metric.Label,
                    value = metric.Value,
                    unit = metric.Unit
                }).ToList()
            },
            BuildChartContextBlock(
                "level_distribution",
                "日志级别分布",
                "Pie",
                ["level", "count"],
                levelDistribution,
                "level"),
            BuildChartContextBlock(
                "time_distribution",
                "日志时间分布",
                "Bar",
                ["time_bucket", "count"],
                timeDistribution,
                "time_bucket"),
            BuildChartContextBlock(
                "issue_category_ranking",
                "问题关键词分类",
                "Bar",
                ["category", "count"],
                issueCategories,
                "category"),
            new
            {
                type = "EvidenceTableBlock",
                id = "device_log_evidence_table",
                title = "设备日志证据表",
                columns = EvidenceColumns.Select(column => new
                {
                    key = column.Key,
                    label = column.Label,
                    data_type = column.DataType
                }).ToList(),
                rows = evidenceRows,
                displayed_row_count = evidenceRows.Count,
                source_row_count = rows.Count,
                limit_note = rows.Count > evidenceRows.Count ? $"证据表仅展示前 {evidenceRows.Count} 条返回记录。" : null
            }
        ];
    }

    public static IReadOnlyList<IWidget> BuildWidgets(
        SemanticQueryPlan plan,
        SemanticSummaryDto semanticSummary,
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(semanticSummary);
        ArgumentNullException.ThrowIfNull(rows);

        if (plan.Target != SemanticQueryTarget.DeviceLog)
        {
            return [];
        }

        var totalCount = rows.Count;
        return
        [
            new StatsCardWidget
            {
                Title = "设备日志总数",
                Description = semanticSummary.Scope,
                Data = new StatsCardData
                {
                    Label = "日志总数",
                    Value = totalCount,
                    Unit = "条"
                }
            },
            BuildChartWidget(
                "日志级别分布",
                "按本次返回记录的日志级别统计。",
                ChartCategory.Pie,
                ["level", "count"],
                BuildLevelDistribution(rows),
                "level"),
            BuildChartWidget(
                "日志时间分布",
                "按小时聚合本次返回记录。",
                ChartCategory.Bar,
                ["time_bucket", "count"],
                BuildTimeDistribution(rows),
                "time_bucket"),
            BuildChartWidget(
                "问题关键词分类",
                "按日志内容关键词做确定性分类统计。",
                ChartCategory.Bar,
                ["category", "count"],
                BuildIssueCategories(rows),
                "category"),
            new DataTableWidget
            {
                Title = "设备日志证据表",
                Description = rows.Count > EvidenceRowLimit ? $"展示前 {EvidenceRowLimit} 条返回记录。" : null,
                Data = new DataTableData
                {
                    Columns = EvidenceColumns.Select(column => new TableColumn
                    {
                        Key = column.Key,
                        Label = column.Label,
                        DataType = column.DataType
                    }).ToList(),
                    Rows = BuildEvidenceRows(rows)
                }
            }
        ];
    }

    private static object BuildChartContextBlock(
        string id,
        string title,
        string category,
        IReadOnlyList<string> dimensions,
        IReadOnlyList<Dictionary<string, object?>> source,
        string xField)
    {
        return new
        {
            type = "ChartBlock",
            id,
            title,
            chart = new
            {
                category,
                dataset = new
                {
                    dimensions,
                    source
                },
                encoding = new
                {
                    x = xField,
                    y = new[] { "count" }
                }
            }
        };
    }

    private static ChartWidget BuildChartWidget(
        string title,
        string description,
        ChartCategory category,
        IReadOnlyList<string> dimensions,
        IReadOnlyList<Dictionary<string, object?>> source,
        string xField)
    {
        return new ChartWidget
        {
            Title = title,
            Description = description,
            Data = new ChartData
            {
                Category = category,
                Dataset = new ChartDataset
                {
                    Dimensions = dimensions.ToList(),
                    Source = source.ToList()
                },
                Encoding = new ChartEncoding
                {
                    X = xField,
                    Y = ["count"]
                }
            }
        };
    }

    private static IReadOnlyList<DisplayMetric> BuildMetrics(
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var errorCount = rows.Count(row => string.Equals(NormalizeLevel(GetString(row, "level")), "ERROR", StringComparison.Ordinal));
        var warnCount = rows.Count(row => string.Equals(NormalizeLevel(GetString(row, "level")), "WARN", StringComparison.Ordinal));
        var latestOccurredAt = rows
            .Select(row => TryGetTimestamp(row, "occurredAt", out var timestamp) ? timestamp : (DateTimeOffset?)null)
            .Where(timestamp => timestamp.HasValue)
            .Select(timestamp => timestamp!.Value)
            .DefaultIfEmpty()
            .Max();

        var metrics = new List<DisplayMetric>
        {
            new("totalCount", "日志总数", rows.Count, "条"),
            new("errorCount", "ERROR", errorCount, "条"),
            new("warnCount", "WARN", warnCount, "条")
        };

        if (latestOccurredAt != default)
        {
            metrics.Add(new DisplayMetric(
                "latestOccurredAt",
                "最新时间",
                latestOccurredAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture),
                null));
        }

        return metrics;
    }

    private static List<Dictionary<string, object?>> BuildLevelDistribution(
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        return rows
            .GroupBy(row => NormalizeLevel(GetString(row, "level")), StringComparer.Ordinal)
            .OrderBy(group => GetLevelSortOrder(group.Key))
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new Dictionary<string, object?>
            {
                ["level"] = group.Key,
                ["count"] = group.Count()
            })
            .ToList();
    }

    private static List<Dictionary<string, object?>> BuildTimeDistribution(
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        return rows
            .Select(row => TryGetTimestamp(row, "occurredAt", out var timestamp) ? timestamp : (DateTimeOffset?)null)
            .Where(timestamp => timestamp.HasValue)
            .Select(timestamp => timestamp!.Value.ToUniversalTime())
            .GroupBy(timestamp => new DateTimeOffset(
                timestamp.Year,
                timestamp.Month,
                timestamp.Day,
                timestamp.Hour,
                0,
                0,
                TimeSpan.Zero))
            .OrderBy(group => group.Key)
            .Select(group => new Dictionary<string, object?>
            {
                ["time_bucket"] = group.Key.ToString("yyyy-MM-dd HH:00 'UTC'", CultureInfo.InvariantCulture),
                ["count"] = group.Count()
            })
            .ToList();
    }

    private static List<Dictionary<string, object?>> BuildIssueCategories(
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        return rows
            .GroupBy(row => ClassifyIssueCategory(GetString(row, "message")), StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new Dictionary<string, object?>
            {
                ["category"] = group.Key,
                ["count"] = group.Count()
            })
            .ToList();
    }

    private static List<Dictionary<string, object?>> BuildEvidenceRows(
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        return rows
            .Take(EvidenceRowLimit)
            .Select(row => new Dictionary<string, object?>
            {
                ["occurredAt"] = FormatTimestamp(row, "occurredAt"),
                ["level"] = NormalizeLevel(GetString(row, "level")),
                ["deviceCode"] = SanitizeTextValue(GetString(row, "deviceCode")),
                ["deviceName"] = SanitizeTextValue(GetString(row, "deviceName")),
                ["processName"] = SanitizeTextValue(GetString(row, "processName")),
                ["message"] = SanitizeTextValue(GetString(row, "message")),
                ["source"] = SanitizeTextValue(GetString(row, "source"))
            })
            .ToList();
    }

    private static string NormalizeLevel(string? value)
    {
        var safeValue = SanitizeTextValue(value);
        return string.IsNullOrWhiteSpace(safeValue)
            ? "UNKNOWN"
            : safeValue.ToUpperInvariant();
    }

    private static int GetLevelSortOrder(string level)
    {
        return level switch
        {
            "ERROR" => 0,
            "WARN" => 1,
            "INFO" => 2,
            "DEBUG" => 3,
            _ => 10
        };
    }

    private static string ClassifyIssueCategory(string? message)
    {
        var safeMessage = SanitizeTextValue(message);
        if (string.IsNullOrWhiteSpace(safeMessage) || string.Equals(safeMessage, RedactedValue, StringComparison.Ordinal))
        {
            return "其他";
        }

        foreach (var rule in IssueCategoryRules)
        {
            if (rule.Keywords.Any(keyword => safeMessage.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return rule.Label;
            }
        }

        return "其他";
    }

    private static string? FormatTimestamp(IReadOnlyDictionary<string, object?> row, string key)
    {
        return TryGetTimestamp(row, key, out var timestamp)
            ? timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
            : SanitizeTextValue(GetString(row, key));
    }

    private static bool TryGetTimestamp(
        IReadOnlyDictionary<string, object?> row,
        string key,
        out DateTimeOffset timestamp)
    {
        if (!TryGetValue(row, key, out var value) || value is null or DBNull)
        {
            timestamp = default;
            return false;
        }

        switch (value)
        {
            case DateTimeOffset dateTimeOffset:
                timestamp = dateTimeOffset;
                return true;

            case DateTime dateTime:
                timestamp = dateTime.Kind == DateTimeKind.Unspecified
                    ? new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc))
                    : new DateTimeOffset(dateTime.ToUniversalTime(), TimeSpan.Zero);
                return true;
        }

        return DateTimeOffset.TryParse(
            value.ToString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out timestamp);
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> row, string key)
    {
        return TryGetValue(row, key, out var value) && value is not null and not DBNull
            ? value.ToString()
            : null;
    }

    private static bool TryGetValue(
        IReadOnlyDictionary<string, object?> row,
        string key,
        out object? value)
    {
        if (row.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (var pair in row)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
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

    private static readonly EvidenceColumn[] EvidenceColumns =
    [
        new("occurredAt", "时间", "date"),
        new("level", "级别", "string"),
        new("deviceCode", "设备编码", "string"),
        new("deviceName", "设备名称", "string"),
        new("processName", "工序", "string"),
        new("message", "日志内容", "string"),
        new("source", "来源", "string")
    ];

    private sealed record DisplayMetric(string Name, string Label, object Value, string? Unit);

    private sealed record EvidenceColumn(string Key, string Label, string DataType);

    private sealed record IssueCategoryRule(string Label, IReadOnlyList<string> Keywords);
}
