using System.Text.Json;
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
        var queryJson = JsonSerializer.Serialize(query with { RawText = goal }, CloudReadonlySimulationQuery.JsonOptions);
        return Result.Success(new CloudReadonlyAgentPlanIntent(
            intent,
            queryJson,
            0.95,
            target,
            kind,
            $"sourceMode=Simulation; target={target}; kind={kind}; lineName={query.LineName ?? "ALL"}; days={query.Days ?? 7}; limit={query.Limit ?? 20}"));
    }

    private static string ResolveIntent(string goal)
    {
        if (ContainsAny(goal, "周报", "weekly", "report", "产线运行"))
        {
            return "Analysis.Line.WeeklyReport";
        }

        if (ContainsAny(goal, "质量", "缺陷", "defect", "quality"))
        {
            return "Analysis.Quality.Defect";
        }

        if (ContainsAny(goal, "工单", "维修", "维护", "maintenance", "work order"))
        {
            return "Analysis.WorkOrder.Maintenance";
        }

        if (ContainsAny(goal, "产能", "趋势", "capacity", "trend"))
        {
            return "Analysis.Capacity.Trend";
        }

        if (ContainsAny(goal, "日志", "告警", "报警", "log", "alarm"))
        {
            return "Analysis.DeviceLog.Recent";
        }

        return "Analysis.Device.Status";
    }

    private static (string Target, string Kind) ResolveTargetKind(string intent)
    {
        return intent switch
        {
            "Analysis.Line.WeeklyReport" => ("Line", "WeeklyReport"),
            "Analysis.Quality.Defect" => ("Quality", "Defect"),
            "Analysis.WorkOrder.Maintenance" => ("WorkOrder", "Maintenance"),
            "Analysis.Capacity.Trend" => ("Capacity", "Trend"),
            "Analysis.DeviceLog.Recent" => ("DeviceLog", "Recent"),
            _ => ("Device", "Status")
        };
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}
