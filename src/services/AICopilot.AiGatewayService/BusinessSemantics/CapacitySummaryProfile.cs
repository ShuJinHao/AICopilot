using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;

namespace AICopilot.AiGatewayService.BusinessSemantics;

internal sealed class CapacitySummaryProfile : SemanticSummaryProfileBase
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
