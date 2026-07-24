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
        ["barcode"] = "弹夹号",
        ["result"] = "生产结果",
        ["startTime"] = "开始时间",
        ["punchingQuantity"] = "冲切数量",
        ["punchingSpeed"] = "冲切速度",
        ["completedAt"] = "完成时间"
    };

    public override SemanticQueryTarget Target => SemanticQueryTarget.ProductionData;

    public override IReadOnlyList<string> ExampleQuestions { get; } =
    [
        "查询今天正极模切05的弹夹、冲切数量和速度",
        "查看负极模切最新生产记录",
        "查看正极模切在指定时间范围内的生产记录"
    ];

    protected override IReadOnlyDictionary<string, string> FieldLabels => Labels;

    public override SemanticSummaryDto Build(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string scope)
    {
        var passCount = rows.Count(row => IsResult(row, "OK", "Pass"));
        var failCount = rows.Count(row => IsResult(row, "NG", "Fail"));
        var passRate = rows.Count == 0
            ? 0m
            : Math.Round(passCount / (decimal)rows.Count * 100m, 2, MidpointRounding.AwayFromZero);
        var groupBreakdown = BuildBreakdown(rows);

        var metrics = new List<SemanticMetricItemDto>
        {
            Metric("totalCount", "记录总数", $"{rows.Count} 条"),
            Metric("passCount", "OK", $"{passCount} 条"),
            Metric("failCount", "NG", $"{failCount} 条"),
            Metric("passRate", "通过率", $"{passRate:F2}%")
        };

        if (!string.IsNullOrWhiteSpace(groupBreakdown))
        {
            metrics.Add(Metric("groupBreakdown", "分组摘要", groupBreakdown));
        }

        var conclusion = $"当前命中 {rows.Count} 条生产记录，OK {passCount} 条，NG {failCount} 条，通过率 {passRate:F2}%。";
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
        return $"客户端 {SemanticSummaryFormatting.GetString(row, "deviceName")}，PLC {GetProductionField(row, "plcName")}，弹夹号 {SemanticSummaryFormatting.GetString(row, "barcode")}，冲切数量 {GetProductionField(row, "punchingQuantity")}，冲切速度 {GetProductionField(row, "punchingSpeed")}，结果 {SemanticSummaryFormatting.GetString(row, "result")}，开始时间 {FormatProductionTimestamp(row, "startTime")}，完成时间 {SemanticSummaryFormatting.FormatTimestamp(SemanticSummaryFormatting.GetString(row, "completedAt"))}";
    }

    private static bool IsResult(Dictionary<string, object?> row, params string[] acceptedValues)
    {
        var result = SemanticSummaryFormatting.GetString(row, "result");
        return acceptedValues.Contains(result, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetProductionField(Dictionary<string, object?> row, string field)
    {
        if (!row.TryGetValue("fields", out var fieldsValue) ||
            fieldsValue is not IReadOnlyDictionary<string, object?> fields ||
            !fields.TryGetValue(field, out var value) ||
            value is null)
        {
            return "-";
        }

        return value switch
        {
            decimal decimalValue => SemanticSummaryFormatting.FormatNumber(decimalValue),
            double doubleValue => SemanticSummaryFormatting.FormatNumber(Convert.ToDecimal(doubleValue)),
            float floatValue => SemanticSummaryFormatting.FormatNumber(Convert.ToDecimal(floatValue)),
            int intValue => intValue.ToString(),
            long longValue => longValue.ToString(),
            _ => value.ToString() ?? "-"
        };
    }

    private static string FormatProductionTimestamp(Dictionary<string, object?> row, string field)
    {
        var value = GetProductionField(row, field);
        return value == "-" ? value : SemanticSummaryFormatting.FormatTimestamp(value);
    }
}
