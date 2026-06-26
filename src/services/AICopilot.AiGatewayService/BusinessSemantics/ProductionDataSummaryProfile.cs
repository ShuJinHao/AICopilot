using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;

namespace AICopilot.AiGatewayService.BusinessSemantics;

internal sealed class ProductionDataSummaryProfile : SemanticSummaryProfileBase
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
