using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;

namespace AICopilot.AiGatewayService.BusinessSemantics;

internal sealed class CapacitySummaryProfile : SemanticSummaryProfileBase
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["shiftDate"] = "时间",
        ["occurredAt"] = "时间",
        ["plcName"] = "PLC 名称",
        ["outputQty"] = "完工弹夹数",
        ["qualifiedQty"] = "合格完工弹夹数",
        ["ngCount"] = "不合格完工弹夹数"
    };

    public override SemanticQueryTarget Target => SemanticQueryTarget.Capacity;

    public override IReadOnlyList<string> ExampleQuestions { get; } =
    [
        "查看设备 DEV-001 在 2026-04-20T00:00:00Z 到 2026-04-21T23:59:59Z 的产能",
        "查看设备 DEV-001 的产能"
    ];

    protected override IReadOnlyDictionary<string, string> FieldLabels => Labels;

    public override SemanticSummaryDto Build(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string scope)
    {
        var totalOutputQty = rows.Sum(row => SemanticSummaryFormatting.GetDecimal(row, "outputQty"));
        var qualifiedQuantities = rows
            .Select(row => SemanticSummaryFormatting.GetNullableDecimal(row, "qualifiedQty"))
            .ToArray();
        decimal? totalQualifiedQty = qualifiedQuantities.All(value => value.HasValue)
            ? qualifiedQuantities.Sum(value => value!.Value)
            : null;
        decimal? qualifiedRate = totalQualifiedQty.HasValue
            ? totalOutputQty <= 0
                ? 0m
                : Math.Round(
                    totalQualifiedQty.Value / totalOutputQty * 100m,
                    2,
                    MidpointRounding.AwayFromZero)
            : null;
        var totalQualifiedQtyText = totalQualifiedQty.HasValue
            ? $"{SemanticSummaryFormatting.FormatNumber(totalQualifiedQty.Value)} 个"
            : "未知";
        var qualifiedRateText = qualifiedRate.HasValue
            ? $"{qualifiedRate.Value:F2}%"
            : "未知";
        var breakdownField = rows.Any(row =>
            SemanticSummaryFormatting.GetString(row, "plcName") != "-")
            ? "plcName"
            : "shiftDate";
        var groupBreakdown = SemanticSummaryFormatting.BuildBreakdown(rows, breakdownField, "条");

        var metrics = new List<SemanticMetricItemDto>
        {
            Metric("totalOutputQty", "完工弹夹数", $"{SemanticSummaryFormatting.FormatNumber(totalOutputQty)} 个"),
            Metric("totalQualifiedQty", "合格完工弹夹数", totalQualifiedQtyText),
            Metric("qualifiedRate", "合格率", qualifiedRateText)
        };

        if (!string.IsNullOrWhiteSpace(groupBreakdown))
        {
            metrics.Add(Metric("groupBreakdown", "分组摘要", groupBreakdown));
        }

        var conclusion = $"当前命中 {rows.Count} 条产能记录，完工弹夹数 {SemanticSummaryFormatting.FormatNumber(totalOutputQty)} 个，合格完工弹夹数 {totalQualifiedQtyText}，合格率 {qualifiedRateText}。";
        var highlights = rows.Take(3).Select(Describe).ToArray();
        return new SemanticSummaryDto(plan.Target.ToString(), conclusion, metrics, highlights, scope);
    }

    private static string Describe(Dictionary<string, object?> row)
    {
        var qualifiedQty = SemanticSummaryFormatting.GetNullableDecimal(row, "qualifiedQty");
        var qualifiedQtyText = qualifiedQty.HasValue
            ? $"{SemanticSummaryFormatting.FormatNumber(qualifiedQty.Value)} 个"
            : "未知";
        return $"时间 {SemanticSummaryFormatting.FormatTimestamp(SemanticSummaryFormatting.GetString(row, "occurredAt"))}，PLC {SemanticSummaryFormatting.GetString(row, "plcName")}，完工弹夹数 {SemanticSummaryFormatting.FormatNumber(SemanticSummaryFormatting.GetDecimal(row, "outputQty"))} 个，合格完工弹夹数 {qualifiedQtyText}";
    }
}
