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
        ["plcCode"] = "PLC 编码",
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
        var allRowsAreHourly = rows.All(row =>
            row.ContainsKey("okRate") && row.ContainsKey("plcCode"));
        decimal? qualifiedRate = allRowsAreHourly
            ? CalculateHourlyQualifiedRate(rows, totalOutputQty)
            : totalQualifiedQty.HasValue
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
        var groupBreakdown = BuildGroupBreakdown(rows);

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
        return $"时间 {SemanticSummaryFormatting.FormatTimestamp(SemanticSummaryFormatting.GetString(row, "occurredAt"))}，PLC {GetPlcIdentity(row)}，完工弹夹数 {SemanticSummaryFormatting.FormatNumber(SemanticSummaryFormatting.GetDecimal(row, "outputQty"))} 个，合格完工弹夹数 {qualifiedQtyText}";
    }

    private static decimal? CalculateHourlyQualifiedRate(
        IReadOnlyList<Dictionary<string, object?>> rows,
        decimal totalOutputQty)
    {
        var rates = rows
            .Select(row => SemanticSummaryFormatting.GetNullableDecimal(row, "okRate"))
            .ToArray();
        if (rates.Any(rate => !rate.HasValue))
        {
            return null;
        }

        if (rows.Count == 1)
        {
            return Math.Round(rates[0]!.Value, 2, MidpointRounding.AwayFromZero);
        }

        if (totalOutputQty <= 0)
        {
            return null;
        }

        var weightedRate = rows
            .Select((row, index) =>
                SemanticSummaryFormatting.GetDecimal(row, "outputQty") * rates[index]!.Value)
            .Sum() / totalOutputQty;
        return Math.Round(weightedRate, 2, MidpointRounding.AwayFromZero);
    }

    private static string BuildGroupBreakdown(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var plcIdentities = rows
            .Select(GetPlcIdentity)
            .Where(value => value != "-")
            .ToArray();
        if (plcIdentities.Length == 0)
        {
            return SemanticSummaryFormatting.BuildBreakdown(rows, "shiftDate", "条");
        }

        return string.Join("，", plcIdentities
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key} {group.Count()}条"));
    }

    private static string GetPlcIdentity(Dictionary<string, object?> row)
    {
        var plcName = SemanticSummaryFormatting.GetString(row, "plcName");
        return !string.IsNullOrWhiteSpace(plcName) && plcName != "-"
            ? plcName
            : SemanticSummaryFormatting.GetString(row, "plcCode");
    }
}
