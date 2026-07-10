using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;

namespace AICopilot.AiGatewayService.BusinessSemantics;

internal sealed class DeviceLogSummaryProfile : SemanticSummaryProfileBase
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["deviceId"] = "设备标识",
        ["deviceName"] = "设备名称",
        ["level"] = "日志级别",
        ["message"] = "日志内容",
        ["occurredAt"] = "日志时间",
        ["receivedAt"] = "Cloud 接收时间"
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
        var deviceName = SemanticSummaryFormatting.GetString(row, "deviceName");
        var deviceSegment = deviceName == "-"
            ? $"设备 {SemanticSummaryFormatting.GetString(row, "deviceId")}"
            : $"设备 {SemanticSummaryFormatting.GetString(row, "deviceId")}（{deviceName}）";
        return $"{deviceSegment}，日志级别 {SemanticSummaryFormatting.GetString(row, "level")}，日志内容 {SemanticSummaryFormatting.GetString(row, "message")}，时间 {SemanticSummaryFormatting.FormatTimestamp(SemanticSummaryFormatting.GetString(row, "occurredAt"))}";
    }
}
