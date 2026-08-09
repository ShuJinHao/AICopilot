using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;

namespace AICopilot.AiGatewayService.BusinessSemantics;

internal sealed class ProductionDataSummaryProfile : SemanticSummaryProfileBase
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["deviceId"] = "设备标识",
        ["deviceName"] = "设备名称",
        ["typeKey"] = "生产数据类型编码",
        ["typeName"] = "生产数据类型名称",
        ["plcCode"] = "PLC 编码",
        ["plcName"] = "PLC 名称",
        ["barcode"] = "业务编号",
        ["result"] = "生产结果",
        ["completedAt"] = "完成时间"
    };

    public override SemanticQueryTarget Target => SemanticQueryTarget.ProductionData;

    public override IReadOnlyList<string> ExampleQuestions { get; } =
    [
        "查询今天指定设备插件和 PLC 的生产记录",
        "查看所选业务记录类别的最新数据",
        "查看已确认设备范围在指定时间内的生产记录"
    ];

    protected override IReadOnlyDictionary<string, string> FieldLabels => Labels;

    public override SemanticSummaryDto Build(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string scope)
    {
        var passCount = rows.Count(row => IsResult(row, "OK", "Pass"));
        var failCount = rows.Count(row => IsResult(row, "NG", "Fail"));
        var categorizedCount = passCount + failCount;
        var passRate = categorizedCount == 0
            ? 0m
            : Math.Round(passCount / (decimal)categorizedCount * 100m, 2, MidpointRounding.AwayFromZero);
        var groupBreakdown = BuildBreakdown(rows);

        var metrics = new List<SemanticMetricItemDto>
        {
            Metric("totalCount", "记录总数", $"{rows.Count} 条")
        };

        if (categorizedCount > 0)
        {
            metrics.Add(Metric("passCount", "OK", $"{passCount} 条"));
            metrics.Add(Metric("failCount", "NG", $"{failCount} 条"));
            metrics.Add(Metric("passRate", "通过率", $"{passRate:F2}%"));
        }

        if (!string.IsNullOrWhiteSpace(groupBreakdown))
        {
            metrics.Add(Metric("groupBreakdown", "分组摘要", groupBreakdown));
        }

        var conclusion = categorizedCount > 0
            ? $"当前命中 {rows.Count} 条生产记录，其中可判定结果 {categorizedCount} 条：OK {passCount} 条，NG {failCount} 条，通过率 {passRate:F2}%。"
            : $"当前命中 {rows.Count} 条生产记录；该记录类别没有返回可判定的 OK/NG 结果。";
        var highlights = rows.Take(3).Select(Describe).ToArray();
        return new SemanticSummaryDto(plan.Target.ToString(), conclusion, metrics, highlights, scope);
    }

    private static string BuildBreakdown(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var preferredField = rows.Any(row =>
            SemanticSummaryFormatting.GetString(row, "typeName") != "-")
            ? "typeName"
            : "typeKey";

        return SemanticSummaryFormatting.BuildBreakdown(rows, preferredField, "条");
    }

    private static string Describe(Dictionary<string, object?> row)
    {
        var parts = new List<string>
        {
            $"设备 {SemanticSummaryFormatting.GetString(row, "deviceName")}",
            $"记录类别 {GetTypeLabel(row)}"
        };

        AppendCoreValue(parts, row, "barcode", "业务编号");
        AppendCoreValue(parts, row, "result", "结果");

        foreach (var schema in GetFieldSchema(row).Take(8))
        {
            if (!TryGetProductionField(row, schema.Key, out var value))
            {
                continue;
            }

            parts.Add($"{schema.Label} {FormatSchemaValue(value, schema)}");
        }

        var completedAt = SemanticSummaryFormatting.GetString(row, "completedAt");
        if (completedAt != "-")
        {
            parts.Add($"完成时间 {SemanticSummaryFormatting.FormatTimestamp(completedAt)}");
        }

        return string.Join("，", parts);
    }

    private static string GetTypeLabel(Dictionary<string, object?> row)
    {
        var typeName = SemanticSummaryFormatting.GetString(row, "typeName");
        return typeName == "-"
            ? SemanticSummaryFormatting.GetString(row, "typeKey")
            : typeName;
    }

    private static void AppendCoreValue(
        ICollection<string> parts,
        Dictionary<string, object?> row,
        string field,
        string label)
    {
        var value = SemanticSummaryFormatting.GetString(row, field);
        if (value != "-")
        {
            parts.Add($"{label} {value}");
        }
    }

    private static IReadOnlyList<CloudAiReadProductionFieldSchemaDto> GetFieldSchema(
        Dictionary<string, object?> row)
    {
        return row.TryGetValue("fieldSchema", out var schemaValue) &&
               schemaValue is IReadOnlyList<CloudAiReadProductionFieldSchemaDto> schema
            ? schema
            : [];
    }

    private static bool TryGetProductionField(
        Dictionary<string, object?> row,
        string field,
        out object value)
    {
        value = null!;
        if (!row.TryGetValue("fields", out var fieldsValue) ||
            fieldsValue is not IReadOnlyDictionary<string, object?> fields ||
            !fields.TryGetValue(field, out var fieldValue) ||
            fieldValue is null)
        {
            return false;
        }

        value = fieldValue;
        return true;
    }

    private static string FormatSchemaValue(
        object value,
        CloudAiReadProductionFieldSchemaDto schema)
    {
        var formatted = value switch
        {
            DateTimeOffset dateTimeOffset => SemanticSummaryFormatting.FormatTimestamp(dateTimeOffset),
            DateTime dateTime => SemanticSummaryFormatting.FormatTimestamp(new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc))),
            decimal decimalValue => FormatNumber(decimalValue, schema.Precision),
            double doubleValue => FormatNumber(Convert.ToDecimal(doubleValue), schema.Precision),
            float floatValue => FormatNumber(Convert.ToDecimal(floatValue), schema.Precision),
            int intValue => intValue.ToString(),
            long longValue => longValue.ToString(),
            bool boolValue => SemanticSummaryFormatting.FormatBoolean(boolValue),
            _ when schema.Type.Contains("time", StringComparison.OrdinalIgnoreCase) ||
                   schema.Type.Contains("date", StringComparison.OrdinalIgnoreCase) =>
                SemanticSummaryFormatting.FormatTimestamp(value.ToString() ?? "-"),
            _ => value.ToString() ?? "-"
        };

        return string.IsNullOrWhiteSpace(schema.Unit)
            ? formatted
            : $"{formatted} {schema.Unit}";
    }

    private static string FormatNumber(decimal value, int? precision)
    {
        if (precision is not >= 0)
        {
            return SemanticSummaryFormatting.FormatNumber(value);
        }

        return value.ToString($"F{Math.Min(precision.Value, 10)}", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsResult(Dictionary<string, object?> row, params string[] acceptedValues)
    {
        var result = SemanticSummaryFormatting.GetString(row, "result");
        return acceptedValues.Contains(result, StringComparer.OrdinalIgnoreCase);
    }
}
