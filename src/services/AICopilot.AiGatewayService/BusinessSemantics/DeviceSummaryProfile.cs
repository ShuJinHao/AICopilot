using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;

namespace AICopilot.AiGatewayService.BusinessSemantics;

internal sealed class DeviceSummaryProfile : SemanticSummaryProfileBase
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
