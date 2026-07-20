using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class CloudReadonlySimulationIntentPlanner : ICloudReadonlySimulationIntentPlanner
{
    public Result<CloudReadonlyAgentPlanIntent> CreateIntent(string goal)
    {
        var query = CloudReadonlySimulationQuery.FromText(goal);
        var intent = ResolveIntent(goal);
        var (target, kind) = ResolveTargetKind(intent);
        var plan = new SemanticQueryPlan(
            intent,
            target,
            kind,
            QueryText: null,
            new SemanticProjection([]),
            BuildFilters(query),
            query.StartAt is null && query.EndAt is null
                ? null
                : new SemanticTimeRange("occurredAt", query.StartAt, query.EndAt),
            Sort: null,
            CloudAiReadRowLimitPolicy.Normalize(query.Limit.GetValueOrDefault(20)));
        return Result.Success(CloudReadonlyAgentPlanIntent.FromSemanticPlan(plan, 0.95));
    }

    private static string ResolveIntent(string goal)
    {
        if (ContainsAny(goal, "周报", "weekly", "report", "产线运行"))
        {
            return "Analysis.Capacity.Range";
        }

        if (ContainsAny(goal, "质量", "缺陷", "defect", "quality"))
        {
            return "Analysis.ProductionData.Range";
        }

        if (ContainsAny(goal, "工单", "维修", "维护", "maintenance", "work order"))
        {
            return "Analysis.DeviceLog.Range";
        }

        if (ContainsAny(goal, "产能", "趋势", "capacity", "trend"))
        {
            return "Analysis.Capacity.Range";
        }

        if (ContainsAny(goal, "日志", "告警", "报警", "log", "alarm"))
        {
            return "Analysis.DeviceLog.Range";
        }

        return "Analysis.Device.Status";
    }

    private static (SemanticQueryTarget Target, SemanticQueryKind Kind) ResolveTargetKind(string intent)
    {
        return intent switch
        {
            "Analysis.Capacity.Range" => (SemanticQueryTarget.Capacity, SemanticQueryKind.Range),
            "Analysis.ProductionData.Range" => (SemanticQueryTarget.ProductionData, SemanticQueryKind.Range),
            "Analysis.DeviceLog.Range" => (SemanticQueryTarget.DeviceLog, SemanticQueryKind.Range),
            _ => (SemanticQueryTarget.Device, SemanticQueryKind.Status)
        };
    }

    private static IReadOnlyList<SemanticFilter> BuildFilters(CloudReadonlySimulationQuery query)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["lineName"] = query.LineName,
            ["deviceCode"] = query.DeviceCode,
            ["level"] = query.Level,
            ["shift"] = query.Shift,
            ["productCode"] = query.ProductCode,
            ["defectType"] = query.DefectType,
            ["status"] = query.Status,
            ["days"] = query.Days?.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return values
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new SemanticFilter(pair.Key, SemanticFilterOperator.Equal, pair.Value!))
            .ToArray();
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}
