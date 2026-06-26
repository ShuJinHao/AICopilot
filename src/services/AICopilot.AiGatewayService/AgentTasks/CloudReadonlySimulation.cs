using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class SimulationCloudReadonlyDataProvider(
    CloudReadonlySimulationDataSet dataSet,
    IOptions<CloudReadonlyOptions> options) : ICloudReadonlyDataProvider
{
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

        var query = CloudReadonlySimulationQuery.Parse(request.Query);
        var result = executor.Execute(request.Intent, query);
        return Task.FromResult(result);
    }
}
