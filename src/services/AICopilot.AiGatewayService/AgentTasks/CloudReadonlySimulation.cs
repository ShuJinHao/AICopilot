using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class SimulationCloudReadonlyDataProvider(
    CloudReadonlySimulationDataSet dataSet,
    IOptions<CloudReadonlyOptions> options) : ICloudReadonlyDataProvider
{
    private const string SimulationSourceMode = CloudReadonlySourceMarkers.SimulationSourceMode;
    private const string SimulationSourceLabel = CloudReadonlySourceMarkers.SimulationSourceLabel;
    private const bool isSimulation = true;

    private readonly CloudReadonlySimulationQueryExecutor executor = new(dataSet);

    public CloudReadonlyDataSourceMode Mode => CloudReadonlyDataSourceMode.Simulation;

    public Task<CloudReadonlyAgentToolResult> QueryAsync(
        CloudReadonlyAgentToolRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Simulation.Enabled)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.NotConfigured,
                "CloudReadonly Simulation mode requires CloudReadonly:Simulation:Enabled=true.");
        }

        var filters = request.SemanticPlan.Filters
            .GroupBy(filter => filter.Field, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);
        var query = new CloudReadonlySimulationQuery(
            filters.GetValueOrDefault("lineName"),
            filters.GetValueOrDefault("deviceCode"),
            filters.GetValueOrDefault("level"),
            filters.GetValueOrDefault("shift"),
            filters.GetValueOrDefault("productCode"),
            filters.GetValueOrDefault("defectType"),
            filters.GetValueOrDefault("status"),
            int.TryParse(filters.GetValueOrDefault("days"), out var days) ? days : null,
            request.SemanticPlan.TimeRange?.Start,
            request.SemanticPlan.TimeRange?.End,
            request.SemanticPlan.Limit,
            RawText: null);
        var result = executor.Execute(request.Intent, query);
        if (result.SourceMode != SimulationSourceMode ||
            result.SourceLabel != SimulationSourceLabel ||
            result.IsSimulation != isSimulation)
        {
            throw new CloudAiReadException(
                CloudAiReadProblemCodes.NotConfigured,
                "CloudReadonly Simulation result must carry explicit simulation source markers.");
        }

        return Task.FromResult(result);
    }
}
