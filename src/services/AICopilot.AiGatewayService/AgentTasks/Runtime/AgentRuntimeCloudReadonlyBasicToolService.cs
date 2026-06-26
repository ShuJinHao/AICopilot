using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentRuntimeCloudReadonlyBasicToolService(ICloudReadonlyAgentToolExecutor cloudReadonlyToolExecutor)
{
    public async Task<object> QueryCloudReadonlyAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var intent = plan.CloudReadonlyIntent ?? throw new CloudAiReadException(
            AppProblemCodes.CloudReadonlyIntentUnsupported,
            "Cloud readonly intent is missing from the agent plan.");
        var result = await cloudReadonlyToolExecutor.ExecuteAsync(
            new CloudReadonlyAgentToolRequest(intent.Intent, intent.Query, intent.Confidence),
            cancellationToken);

        state.CloudReadonlySummary = result.Summary;
        state.CloudReadonlyRows = result.Rows;
        state.CloudReadonlySourceLabel = result.SourceLabel;
        state.CloudReadonlySourcePath = result.SourcePath;
        state.CloudReadonlySourceMode = result.SourceMode;
        state.CloudReadonlyIsSimulation = result.IsSimulation;
        state.CloudReadonlyRowCount = result.RowCount;
        state.CloudReadonlyIsTruncated = result.IsTruncated;
        return result;
    }
}
