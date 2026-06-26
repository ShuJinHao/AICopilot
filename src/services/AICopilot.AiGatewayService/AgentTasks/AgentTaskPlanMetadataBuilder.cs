using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentTaskPlanMetadataBuilder
{
    private const string SimulationBusinessSourceLabel = "AI 独立模拟业务库";

    public static IReadOnlyCollection<AgentPlannerDataSourceSummary> BuildPlannerDataSourceSummaries(
        IReadOnlyCollection<BusinessDatabaseDescriptor> dataSources)
    {
        return dataSources
            .Select(source => new AgentPlannerDataSourceSummary(
                source.Id,
                source.Name,
                source.ExternalSystemType.ToString(),
                source.BusinessDomain,
                source.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness,
                ResolveSourceLabel(source)))
            .ToArray();
    }

    public static IReadOnlyCollection<AgentTaskPlanDataSourceSummaryDocument> BuildPlanDataSourceSummaries(
        IReadOnlyCollection<BusinessDatabaseDescriptor> dataSources)
    {
        return dataSources
            .Select(source => new AgentTaskPlanDataSourceSummaryDocument(
                source.Id,
                source.Name,
                source.ExternalSystemType.ToString(),
                source.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness,
                ResolveSourceLabel(source),
                source.BusinessDomain))
            .ToArray();
    }

    public static IReadOnlyDictionary<string, int> BuildToolRiskSummary(PlannerToolCatalog? catalog)
    {
        return (catalog?.Tools ?? [])
            .GroupBy(tool => tool.RiskLevel, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    }

    public static AgentTaskRiskLevel DetermineRiskLevel(AgentTaskType taskType)
    {
        return taskType is AgentTaskType.CloudDataReport
            ? AgentTaskRiskLevel.Medium
            : AgentTaskRiskLevel.Low;
    }

    public static string BuildTitle(string goal)
    {
        var normalized = string.Join(' ', (goal ?? string.Empty).Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "A鍔╃悊浠诲姟";
        }

        return normalized.Length <= 48 ? normalized : normalized[..48];
    }

    private static string ResolveSourceLabel(BusinessDatabaseDescriptor source)
    {
        return source.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness
            ? SimulationBusinessSourceLabel
            : source.ExternalSystemType.ToString();
    }
}
