using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentRuntimeCloudReadonlyToolService(
    ICloudReadonlyAgentToolExecutor cloudReadonlyToolExecutor,
    CloudReadonlySandboxAgentTrialService? cloudSandboxAgentTrialService,
    CloudReadonlySandboxControlledTrialService? cloudSandboxControlledTrialService,
    CloudReadonlyProductionPilotService? cloudReadonlyProductionPilotService,
    CloudReadonlyProductionControlledPilotService? cloudReadonlyProductionControlledPilotService,
    CloudReadonlyPilotReadinessService? cloudReadonlyPilotReadinessService,
    IReadRepository<ToolRegistration>? toolReadRepository)
{
    private readonly AgentRuntimeCloudReadonlyBasicToolService basicTools = new(cloudReadonlyToolExecutor);

    private readonly AgentRuntimeCloudReadonlySandboxToolService sandboxTools = new(
        cloudSandboxAgentTrialService,
        cloudSandboxControlledTrialService);

    private readonly AgentRuntimeCloudReadonlyProductionPilotToolService productionPilotTools = new(
        cloudReadonlyProductionPilotService,
        cloudReadonlyProductionControlledPilotService,
        cloudReadonlyPilotReadinessService,
        toolReadRepository);

    public Task<object> QueryCloudReadonlyAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        return basicTools.QueryCloudReadonlyAsync(plan, state, cancellationToken);
    }

    public Task<object> QueryCloudReadonlySandboxAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        AgentStep step,
        CancellationToken cancellationToken)
    {
        return sandboxTools.QueryCloudReadonlySandboxAsync(plan, state, step, cancellationToken);
    }

    public Task<object> QueryCloudReadonlySandboxControlledAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        AgentStep step,
        CancellationToken cancellationToken)
    {
        return sandboxTools.QueryCloudReadonlySandboxControlledAsync(plan, state, step, cancellationToken);
    }

    public Task<object> QueryCloudReadonlyProductionPilotAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        AgentStep step,
        CancellationToken cancellationToken)
    {
        return productionPilotTools.QueryCloudReadonlyProductionPilotAsync(plan, state, step, cancellationToken);
    }

    public Task<object> QueryCloudReadonlyProductionControlledPilotAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        AgentStep step,
        CancellationToken cancellationToken)
    {
        return productionPilotTools.QueryCloudReadonlyProductionControlledPilotAsync(plan, state, step, cancellationToken);
    }
}
