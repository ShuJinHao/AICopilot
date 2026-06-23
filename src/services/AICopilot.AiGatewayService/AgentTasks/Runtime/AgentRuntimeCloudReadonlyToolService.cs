using AICopilot.Core.AiGateway.Aggregates.AgentTasks;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentRuntimeCloudReadonlyToolService(ICloudReadonlyAgentToolExecutor cloudReadonlyToolExecutor)
{
    private readonly AgentRuntimeCloudReadonlyBasicToolService basicTools = new(cloudReadonlyToolExecutor);

    public Task<object> QueryCloudReadonlyAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        return basicTools.QueryCloudReadonlyAsync(plan, state, cancellationToken);
    }
}
