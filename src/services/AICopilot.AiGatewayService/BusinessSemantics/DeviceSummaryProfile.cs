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
        ["processId"] = "工序标识",
        ["clientCode"] = "客户端编码",
        ["softwareStatus"] = "Cloud 权威软件状态",
        ["runtimeStatus"] = "最后上报运行状态",
        ["runtimeStartedAtUtc"] = "本次运行开始时间",
        ["lastRuntimeHeartbeatAtUtc"] = "最后运行心跳时间"
    };

    public override SemanticQueryTarget Target => SemanticQueryTarget.Device;

    public override IReadOnlyList<string> ExampleQuestions { get; } =
    [
        "列出设备主数据",
        "查看设备 DEV-001 的详情",
        "设备 DEV-001 最后上报的运行状态是什么？"
    ];

    protected override IReadOnlyDictionary<string, string> FieldLabels => Labels;

    public override SemanticSummaryDto Build(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string scope)
    {
        if (plan.Kind == SemanticQueryKind.Status)
        {
            return BuildStatusSummary(plan, rows, scope);
        }

        var metrics = new List<SemanticMetricItemDto>
        {
            Metric("totalCount", "设备总数", $"{rows.Count} 台")
        };

        var highlights = rows.Take(3).Select(DescribeMasterData).ToArray();
        return new SemanticSummaryDto(
            plan.Target.ToString(),
            $"当前命中 {rows.Count} 台设备主数据记录。",
            metrics,
            highlights,
            scope);
    }

    private static SemanticSummaryDto BuildStatusSummary(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string scope)
    {
        var missingCount = rows.Count(row => string.Equals(
            SemanticSummaryFormatting.GetString(row, "softwareStatus"),
            "MissingRuntimeHeartbeat",
            StringComparison.OrdinalIgnoreCase));
        var staleCount = rows.Count(row => string.Equals(
            SemanticSummaryFormatting.GetString(row, "softwareStatus"),
            "RuntimeHeartbeatStale",
            StringComparison.OrdinalIgnoreCase));
        var validHeartbeatCount = rows.Count - missingCount - staleCount;
        var statusBreakdown = SemanticSummaryFormatting.BuildBreakdown(rows, "runtimeStatus", "台");
        var metrics = new List<SemanticMetricItemDto>
        {
            Metric("totalCount", "命中设备总数", $"{rows.Count} 台"),
            Metric("missingRuntimeHeartbeatCount", "尚无运行心跳", $"{missingCount} 台"),
            Metric("runtimeHeartbeatStaleCount", "运行心跳已陈旧", $"{staleCount} 台"),
            Metric("validRuntimeHeartbeatCount", "有效运行心跳", $"{validHeartbeatCount} 台")
        };
        if (!string.IsNullOrWhiteSpace(statusBreakdown))
        {
            metrics.Add(Metric("runtimeStatusBreakdown", "最后上报运行状态分布", statusBreakdown));
        }

        var highlights = rows.Take(3).Select(DescribeStatus).ToArray();
        return new SemanticSummaryDto(
            plan.Target.ToString(),
            rows.Count == 0
                ? "当前授权范围内没有匹配的设备。"
                : $"当前命中 {rows.Count} 台设备：{missingCount} 台尚无运行心跳，{staleCount} 台运行心跳已陈旧，{validHeartbeatCount} 台具有有效运行心跳；陈旧不等于离线或停止。",
            metrics,
            highlights,
            scope);
    }

    private static string DescribeMasterData(Dictionary<string, object?> row)
    {
        return $"设备 {SemanticSummaryFormatting.GetString(row, "deviceCode")} / {SemanticSummaryFormatting.GetString(row, "deviceName")}，工序标识 {SemanticSummaryFormatting.GetString(row, "processId")}";
    }

    private static string DescribeStatus(Dictionary<string, object?> row)
    {
        var device = $"设备 {SemanticSummaryFormatting.GetString(row, "clientCode")} / {SemanticSummaryFormatting.GetString(row, "deviceName")}";
        var softwareStatus = SemanticSummaryFormatting.GetString(row, "softwareStatus");
        if (softwareStatus.Equals("MissingRuntimeHeartbeat", StringComparison.OrdinalIgnoreCase))
        {
            return $"{device}存在，但尚无运行心跳。";
        }

        var heartbeat = SemanticSummaryFormatting.FormatTimestamp(
            SemanticSummaryFormatting.GetString(row, "lastRuntimeHeartbeatAtUtc"));
        if (softwareStatus.Equals("RuntimeHeartbeatStale", StringComparison.OrdinalIgnoreCase))
        {
            return $"{device}的最后运行心跳为 {heartbeat}，Cloud 判定 RuntimeHeartbeatStale；这不等于离线或 Stopped。";
        }

        return $"{device}，Cloud 软件状态 {softwareStatus}，最后上报运行状态 {SemanticSummaryFormatting.GetString(row, "runtimeStatus")}，最后心跳 {heartbeat}。";
    }
}
