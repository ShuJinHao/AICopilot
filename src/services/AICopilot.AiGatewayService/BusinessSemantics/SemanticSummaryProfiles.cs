using System.Globalization;
using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;

namespace AICopilot.AiGatewayService.BusinessSemantics;

public interface ISemanticSummaryProfileCatalog
{
    IReadOnlyCollection<ISemanticSummaryProfile> GetAll();

    bool TryGet(SemanticQueryTarget target, out ISemanticSummaryProfile profile);
}

public interface ISemanticSummaryProfile
{
    SemanticQueryTarget Target { get; }

    IReadOnlyList<string> ExampleQuestions { get; }

    SemanticSummaryResponseContract ResponseContract { get; }

    string GetFieldLabel(string field);

    SemanticSummaryDto Build(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string scope);
}

public sealed record SemanticSummaryResponseContract(
    string ConclusionSection,
    string MetricSection,
    string HighlightSection,
    string ScopeSection)
{
    public static SemanticSummaryResponseContract Default { get; } = new(
        "结论",
        "关键指标",
        "关键记录",
        "查询范围");
}

public sealed class SemanticSummaryProfileCatalog : ISemanticSummaryProfileCatalog
{
    private readonly IReadOnlyDictionary<SemanticQueryTarget, ISemanticSummaryProfile> _profiles =
        new ISemanticSummaryProfile[]
        {
            new DeviceSummaryProfile(),
            new DeviceLogSummaryProfile(),
            new RecipeSummaryProfile(),
            new CapacitySummaryProfile(),
            new ProductionDataSummaryProfile()
        }.ToDictionary(item => item.Target);

    public IReadOnlyCollection<ISemanticSummaryProfile> GetAll()
    {
        return _profiles.Values.OrderBy(item => item.Target).ToArray();
    }

    public bool TryGet(SemanticQueryTarget target, out ISemanticSummaryProfile profile)
    {
        return _profiles.TryGetValue(target, out profile!);
    }
}

public abstract class SemanticSummaryProfileBase : ISemanticSummaryProfile
{
    public abstract SemanticQueryTarget Target { get; }

    public abstract IReadOnlyList<string> ExampleQuestions { get; }

    public SemanticSummaryResponseContract ResponseContract { get; } = SemanticSummaryResponseContract.Default;

    protected abstract IReadOnlyDictionary<string, string> FieldLabels { get; }

    public string GetFieldLabel(string field)
    {
        return FieldLabels.TryGetValue(field, out var label)
            ? label
            : field;
    }

    public abstract SemanticSummaryDto Build(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string scope);

    protected static SemanticMetricItemDto Metric(string name, string label, string value)
    {
        return new SemanticMetricItemDto(name, label, value);
    }
}

file sealed class DeviceSummaryProfile : SemanticSummaryProfileBase
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["deviceId"] = "设备标识",
        ["deviceCode"] = "设备编码",
        ["deviceName"] = "设备名称",
        ["status"] = "设备状态",
        ["lineName"] = "产线",
        ["updatedAt"] = "时间"
    };

    public override SemanticQueryTarget Target => SemanticQueryTarget.Device;

    public override IReadOnlyList<string> ExampleQuestions { get; } =
    [
        "列出 LINE-A 产线的设备",
        "查看设备 DEV-001 的详情",
        "设备 DEV-001 现在是什么状态？"
    ];

    protected override IReadOnlyDictionary<string, string> FieldLabels => Labels;

    public override SemanticSummaryDto Build(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string scope)
    {
        var statusBreakdown = SemanticSummaryFormatting.BuildBreakdown(rows, "status", "台");
        var lineBreakdown = SemanticSummaryFormatting.BuildBreakdown(rows, "lineName", "台");
        var metrics = new List<SemanticMetricItemDto>
        {
            Metric("totalCount", "设备总数", $"{rows.Count} 台")
        };

        if (!string.IsNullOrWhiteSpace(statusBreakdown))
        {
            metrics.Add(Metric("statusBreakdown", "状态分布", statusBreakdown));
        }

        if (!string.IsNullOrWhiteSpace(lineBreakdown))
        {
            metrics.Add(Metric("lineBreakdown", "产线分布", lineBreakdown));
        }

        var highlights = rows.Take(3).Select(Describe).ToArray();
        var conclusion = string.IsNullOrWhiteSpace(statusBreakdown)
            ? $"当前命中 {rows.Count} 台设备。"
            : $"当前命中 {rows.Count} 台设备，状态分布已汇总。";

        return new SemanticSummaryDto(plan.Target.ToString(), conclusion, metrics, highlights, scope);
    }

    private static string Describe(Dictionary<string, object?> row)
    {
        return $"设备 {SemanticSummaryFormatting.GetString(row, "deviceCode")} / {SemanticSummaryFormatting.GetString(row, "deviceName")}，状态 {SemanticSummaryFormatting.GetString(row, "status")}，产线 {SemanticSummaryFormatting.GetString(row, "lineName")}，时间 {SemanticSummaryFormatting.FormatTimestamp(SemanticSummaryFormatting.GetString(row, "updatedAt"))}";
    }
}

file sealed class DeviceLogSummaryProfile : SemanticSummaryProfileBase
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["deviceCode"] = "设备编码",
        ["level"] = "日志级别",
        ["message"] = "日志内容",
        ["source"] = "日志来源",
        ["occurredAt"] = "时间"
    };

    public override SemanticQueryTarget Target => SemanticQueryTarget.DeviceLog;

    public override IReadOnlyList<string> ExampleQuestions { get; } =
    [
        "查看设备 DEV-001 最新日志",
        "查看设备 DEV-001 在 2026-04-20T00:00:00Z 到 2026-04-20T23:59:59Z 的日志",
        "查看设备 DEV-001 的错误日志"
    ];

    protected override IReadOnlyDictionary<string, string> FieldLabels => Labels;

    public override SemanticSummaryDto Build(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string scope)
    {
        var levelBreakdown = SemanticSummaryFormatting.BuildBreakdown(rows, "level", "条");
        var latestOccurredAt = rows
            .Select(row => SemanticSummaryFormatting.GetTimestamp(row, "occurredAt"))
            .Where(timestamp => timestamp.HasValue)
            .Select(timestamp => timestamp!.Value)
            .DefaultIfEmpty()
            .Max();

        var metrics = new List<SemanticMetricItemDto>
        {
            Metric("totalCount", "日志总数", $"{rows.Count} 条")
        };

        if (!string.IsNullOrWhiteSpace(levelBreakdown))
        {
            metrics.Add(Metric("levelBreakdown", "级别分布", levelBreakdown));
        }

        if (latestOccurredAt != default)
        {
            metrics.Add(Metric("latestOccurredAt", "最新时间", SemanticSummaryFormatting.FormatTimestamp(latestOccurredAt)));
        }

        var conclusion = latestOccurredAt == default
            ? $"当前命中 {rows.Count} 条设备日志。"
            : $"当前命中 {rows.Count} 条设备日志，最近时间为 {SemanticSummaryFormatting.FormatTimestamp(latestOccurredAt)}。";

        var highlights = rows.Take(3).Select(Describe).ToArray();
        return new SemanticSummaryDto(plan.Target.ToString(), conclusion, metrics, highlights, scope);
    }

    private static string Describe(Dictionary<string, object?> row)
    {
        return $"设备 {SemanticSummaryFormatting.GetString(row, "deviceCode")}，日志级别 {SemanticSummaryFormatting.GetString(row, "level")}，日志内容 {SemanticSummaryFormatting.GetString(row, "message")}，时间 {SemanticSummaryFormatting.FormatTimestamp(SemanticSummaryFormatting.GetString(row, "occurredAt"))}";
    }
}

file sealed class RecipeSummaryProfile : SemanticSummaryProfileBase
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["recipeId"] = "配方标识",
        ["recipeName"] = "配方名称",
        ["deviceCode"] = "设备编码",
        ["processName"] = "工序名称",
        ["version"] = "版本号",
        ["isActive"] = "当前生效版本",
        ["updatedAt"] = "时间"
    };

    public override SemanticQueryTarget Target => SemanticQueryTarget.Recipe;

    public override IReadOnlyList<string> ExampleQuestions { get; } =
    [
        "列出设备 DEV-001 的配方",
        "查看配方 Recipe-Cut-01 详情",
        "查看配方 Recipe-Cut-01 的版本历史"
    ];

    protected override IReadOnlyDictionary<string, string> FieldLabels => Labels;

    public override SemanticSummaryDto Build(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string scope)
    {
        var activeVersion = rows
            .Where(row => SemanticSummaryFormatting.GetBoolean(row, "isActive"))
            .Select(row => SemanticSummaryFormatting.GetString(row, "version"))
            .FirstOrDefault(version => !string.IsNullOrWhiteSpace(version) && version != "-");

        var versionChain = rows
            .Select(row => SemanticSummaryFormatting.GetString(row, "version"))
            .Where(version => !string.IsNullOrWhiteSpace(version) && version != "-")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(SemanticSummaryFormatting.ParseVersionKey)
            .ToArray();

        var metrics = new List<SemanticMetricItemDto>
        {
            Metric("totalCount", "配方总数", $"{rows.Count} 条"),
            Metric("activeVersion", "当前生效版本", string.IsNullOrWhiteSpace(activeVersion) ? "未命中" : activeVersion)
        };

        if (versionChain.Length > 0)
        {
            metrics.Add(Metric("versionChain", "版本链", string.Join(" -> ", versionChain)));
        }

        var recipeName = rows
            .Select(row => SemanticSummaryFormatting.GetString(row, "recipeName"))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && value != "-");

        var conclusion = !string.IsNullOrWhiteSpace(recipeName)
            ? $"当前命中 {rows.Count} 条配方记录，{recipeName} 的当前生效版本为 {activeVersion ?? "未命中"}。"
            : $"当前命中 {rows.Count} 条配方记录。";

        var highlights = rows.Take(3).Select(Describe).ToArray();
        return new SemanticSummaryDto(plan.Target.ToString(), conclusion, metrics, highlights, scope);
    }

    private static string Describe(Dictionary<string, object?> row)
    {
        return $"配方 {SemanticSummaryFormatting.GetString(row, "recipeName")}，版本 {SemanticSummaryFormatting.GetString(row, "version")}，当前生效版本 {SemanticSummaryFormatting.FormatBoolean(SemanticSummaryFormatting.GetBoolean(row, "isActive"))}，设备 {SemanticSummaryFormatting.GetString(row, "deviceCode")}，工序 {SemanticSummaryFormatting.GetString(row, "processName")}，时间 {SemanticSummaryFormatting.FormatTimestamp(SemanticSummaryFormatting.GetString(row, "updatedAt"))}";
    }
}

file sealed class CapacitySummaryProfile : SemanticSummaryProfileBase
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["deviceCode"] = "设备编码",
        ["processName"] = "工序名称",
        ["shiftDate"] = "时间",
        ["occurredAt"] = "时间",
        ["outputQty"] = "总产出",
        ["qualifiedQty"] = "合格数"
    };

    public override SemanticQueryTarget Target => SemanticQueryTarget.Capacity;

    public override IReadOnlyList<string> ExampleQuestions { get; } =
    [
        "查看设备 DEV-001 在 2026-04-20T00:00:00Z 到 2026-04-21T23:59:59Z 的产能",
        "查看设备 DEV-001 的产能",
        "查看 Cutting 工序的产能"
    ];

    protected override IReadOnlyDictionary<string, string> FieldLabels => Labels;

    public override SemanticSummaryDto Build(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string scope)
    {
        var totalOutputQty = rows.Sum(row => SemanticSummaryFormatting.GetDecimal(row, "outputQty"));
        var totalQualifiedQty = rows.Sum(row => SemanticSummaryFormatting.GetDecimal(row, "qualifiedQty"));
        var qualifiedRate = totalOutputQty <= 0
            ? 0m
            : Math.Round(totalQualifiedQty / totalOutputQty * 100m, 2, MidpointRounding.AwayFromZero);
        var groupBreakdown = BuildBreakdown(plan, rows);

        var metrics = new List<SemanticMetricItemDto>
        {
            Metric("totalOutputQty", "总产出", SemanticSummaryFormatting.FormatNumber(totalOutputQty)),
            Metric("totalQualifiedQty", "合格数", SemanticSummaryFormatting.FormatNumber(totalQualifiedQty)),
            Metric("qualifiedRate", "合格率", $"{qualifiedRate:F2}%")
        };

        if (!string.IsNullOrWhiteSpace(groupBreakdown))
        {
            metrics.Add(Metric("groupBreakdown", "分组摘要", groupBreakdown));
        }

        var conclusion = $"当前命中 {rows.Count} 条产能记录，总产出 {SemanticSummaryFormatting.FormatNumber(totalOutputQty)}，合格数 {SemanticSummaryFormatting.FormatNumber(totalQualifiedQty)}，合格率 {qualifiedRate:F2}%。";
        var highlights = rows.Take(3).Select(Describe).ToArray();
        return new SemanticSummaryDto(plan.Target.ToString(), conclusion, metrics, highlights, scope);
    }

    private static string BuildBreakdown(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var groupField = plan.Kind switch
        {
            SemanticQueryKind.ByProcess => "deviceCode",
            SemanticQueryKind.ByDevice => "processName",
            _ => plan.Filters.Any(filter => string.Equals(filter.Field, "processName", StringComparison.OrdinalIgnoreCase))
                ? "deviceCode"
                : "processName"
        };

        return SemanticSummaryFormatting.BuildBreakdown(rows, groupField, "条");
    }

    private static string Describe(Dictionary<string, object?> row)
    {
        return $"设备 {SemanticSummaryFormatting.GetString(row, "deviceCode")}，工序 {SemanticSummaryFormatting.GetString(row, "processName")}，时间 {SemanticSummaryFormatting.FormatTimestamp(SemanticSummaryFormatting.GetString(row, "occurredAt"))}，总产出 {SemanticSummaryFormatting.FormatNumber(SemanticSummaryFormatting.GetDecimal(row, "outputQty"))}，合格数 {SemanticSummaryFormatting.FormatNumber(SemanticSummaryFormatting.GetDecimal(row, "qualifiedQty"))}";
    }
}

file sealed class ProductionDataSummaryProfile : SemanticSummaryProfileBase
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["deviceCode"] = "设备编码",
        ["processName"] = "工序名称",
        ["barcode"] = "条码",
        ["stationName"] = "工位名称",
        ["result"] = "生产结果",
        ["occurredAt"] = "时间"
    };

    public override SemanticQueryTarget Target => SemanticQueryTarget.ProductionData;

    public override IReadOnlyList<string> ExampleQuestions { get; } =
    [
        "查看设备 DEV-001 最新生产记录",
        "查看 DEV-001 在 2026-04-21T00:00:00Z 到 2026-04-21T23:59:59Z 的生产记录",
        "查看设备 DEV-001 的生产记录"
    ];

    protected override IReadOnlyDictionary<string, string> FieldLabels => Labels;

    public override SemanticSummaryDto Build(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string scope)
    {
        var passCount = rows.Count(row => string.Equals(SemanticSummaryFormatting.GetString(row, "result"), "Pass", StringComparison.OrdinalIgnoreCase));
        var failCount = rows.Count(row => string.Equals(SemanticSummaryFormatting.GetString(row, "result"), "Fail", StringComparison.OrdinalIgnoreCase));
        var passRate = rows.Count == 0
            ? 0m
            : Math.Round(passCount / (decimal)rows.Count * 100m, 2, MidpointRounding.AwayFromZero);
        var groupBreakdown = BuildBreakdown(rows);

        var metrics = new List<SemanticMetricItemDto>
        {
            Metric("totalCount", "记录总数", $"{rows.Count} 条"),
            Metric("passCount", "Pass", $"{passCount} 条"),
            Metric("failCount", "Fail", $"{failCount} 条"),
            Metric("passRate", "通过率", $"{passRate:F2}%")
        };

        if (!string.IsNullOrWhiteSpace(groupBreakdown))
        {
            metrics.Add(Metric("groupBreakdown", "分组摘要", groupBreakdown));
        }

        var conclusion = $"当前命中 {rows.Count} 条生产记录，Pass {passCount} 条，Fail {failCount} 条，通过率 {passRate:F2}%。";
        var highlights = rows.Take(3).Select(Describe).ToArray();
        return new SemanticSummaryDto(plan.Target.ToString(), conclusion, metrics, highlights, scope);
    }

    private static string BuildBreakdown(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var preferredField = rows.Any(row =>
            !string.IsNullOrWhiteSpace(SemanticSummaryFormatting.GetString(row, "stationName")) &&
            SemanticSummaryFormatting.GetString(row, "stationName") != "-")
            ? "stationName"
            : "deviceCode";

        return SemanticSummaryFormatting.BuildBreakdown(rows, preferredField, "条");
    }

    private static string Describe(Dictionary<string, object?> row)
    {
        return $"设备 {SemanticSummaryFormatting.GetString(row, "deviceCode")}，工位 {SemanticSummaryFormatting.GetString(row, "stationName")}，条码 {SemanticSummaryFormatting.GetString(row, "barcode")}，生产结果 {SemanticSummaryFormatting.GetString(row, "result")}，时间 {SemanticSummaryFormatting.FormatTimestamp(SemanticSummaryFormatting.GetString(row, "occurredAt"))}";
    }
}

file static class SemanticSummaryFormatting
{
    public static string BuildBreakdown(
        IReadOnlyList<Dictionary<string, object?>> rows,
        string field,
        string unit)
    {
        var breakdown = rows
            .Select(row => GetString(row, field))
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "-")
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key} {group.Count()}{unit}")
            .ToArray();

        return breakdown.Length == 0 ? string.Empty : string.Join("，", breakdown);
    }

    public static string GetString(Dictionary<string, object?> row, string field)
    {
        return row.TryGetValue(field, out var value)
            ? value?.ToString() ?? "-"
            : "-";
    }

    public static decimal GetDecimal(Dictionary<string, object?> row, string field)
    {
        if (!row.TryGetValue(field, out var value) || value == null)
        {
            return 0m;
        }

        return value switch
        {
            decimal decimalValue => decimalValue,
            double doubleValue => Convert.ToDecimal(doubleValue),
            float floatValue => Convert.ToDecimal(floatValue),
            int intValue => intValue,
            long longValue => longValue,
            _ when decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0m
        };
    }

    public static bool GetBoolean(Dictionary<string, object?> row, string field)
    {
        if (!row.TryGetValue(field, out var value) || value == null)
        {
            return false;
        }

        return value switch
        {
            bool boolValue => boolValue,
            int intValue => intValue != 0,
            long longValue => longValue != 0,
            _ when bool.TryParse(value.ToString(), out var parsedBool) => parsedBool,
            _ when int.TryParse(value.ToString(), out var parsedInt) => parsedInt != 0,
            _ => false
        };
    }

    public static DateTimeOffset? GetTimestamp(Dictionary<string, object?> row, string field)
    {
        if (!row.TryGetValue(field, out var value) || value == null)
        {
            return null;
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset;
        }

        if (value is DateTime dateTime)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
        }

        return DateTimeOffset.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    public static string FormatNumber(decimal value)
    {
        return decimal.Truncate(value) == value
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public static string FormatBoolean(bool value)
    {
        return value ? "是" : "否";
    }

    public static string FormatTimestamp(string rawValue)
    {
        return DateTimeOffset.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? FormatTimestamp(parsed)
            : rawValue;
    }

    public static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
    }

    public static VersionKey ParseVersionKey(string version)
    {
        var numbers = new List<int>();
        var current = string.Empty;
        foreach (var ch in version)
        {
            if (char.IsDigit(ch))
            {
                current += ch;
                continue;
            }

            if (current.Length > 0)
            {
                numbers.Add(int.Parse(current, CultureInfo.InvariantCulture));
                current = string.Empty;
            }
        }

        if (current.Length > 0)
        {
            numbers.Add(int.Parse(current, CultureInfo.InvariantCulture));
        }

        return new VersionKey(numbers);
    }
}

file sealed record VersionKey(IReadOnlyList<int> Parts) : IComparable<VersionKey>
{
    public int CompareTo(VersionKey? other)
    {
        if (other is null)
        {
            return 1;
        }

        var maxLength = Math.Max(Parts.Count, other.Parts.Count);
        for (var index = 0; index < maxLength; index++)
        {
            var left = index < Parts.Count ? Parts[index] : 0;
            var right = index < other.Parts.Count ? other.Parts[index] : 0;
            var comparison = left.CompareTo(right);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }
}
