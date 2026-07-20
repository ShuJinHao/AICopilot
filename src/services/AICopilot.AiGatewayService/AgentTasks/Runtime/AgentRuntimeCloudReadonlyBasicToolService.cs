using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
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
            new CloudReadonlyAgentToolRequest(
                intent.ToSemanticPlan(),
                intent.SemanticPlanDigest,
                intent.Confidence),
            cancellationToken);

        state.CloudReadonlySummary = result.Summary;
        state.CloudReadonlyRows = result.Rows;
        state.CloudReadonlySourceLabel = result.SourceLabel;
        state.CloudReadonlySourcePath = result.SourcePath;
        state.CloudReadonlySourceMode = result.SourceMode;
        state.CloudReadonlyIsSimulation = result.IsSimulation;
        state.CloudReadonlyRowCount = result.RowCount;
        state.CloudReadonlyIsTruncated = result.IsTruncated;
        var canonicalResult = AgentCanonicalJsonV1.Canonicalize(
            JsonSerializer.Serialize(result, AgentRuntimeJson.Options));
        var resultHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalResult))).ToLowerInvariant();
        return new
        {
            status = "completed",
            resultType = "cloud-query-summary",
            sourceMode = result.SourceMode,
            isSimulation = result.IsSimulation,
            rowCount = result.RowCount,
            isTruncated = result.IsTruncated,
            resultHash
        };
    }
}
