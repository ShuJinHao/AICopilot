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
        AgentStep step,
        AgentTaskRunState state,
        CancellationToken cancellationToken)
    {
        var node = plan.Nodes?.ElementAtOrDefault(step.StepIndex - 1);
        var intent = node?.Input.SemanticIntent is { } semanticIntent &&
                     node.Input.SemanticPlanDigest is { } semanticPlanDigest
            ? plan.CloudReadonlyIntents?.SingleOrDefault(candidate =>
                string.Equals(candidate.Intent, semanticIntent, StringComparison.Ordinal) &&
                string.Equals(candidate.SemanticPlanDigest, semanticPlanDigest, StringComparison.Ordinal))
            : null;
        intent ??= throw new CloudAiReadException(
            AppProblemCodes.CloudReadonlyIntentUnsupported,
            "Cloud readonly node is not bound to exactly one frozen typed semantic intent.");
        var result = await cloudReadonlyToolExecutor.ExecuteAsync(
            new CloudReadonlyAgentToolRequest(
                intent.ToSemanticPlan(),
                intent.SemanticPlanDigest,
                intent.Confidence),
            cancellationToken);

        state.CloudReadonlyResults.RemoveAll(snapshot =>
            string.Equals(snapshot.Intent, intent.Intent, StringComparison.Ordinal));
        state.CloudReadonlyResults.Add(new AgentCloudReadonlyQuerySnapshot(
            intent.Intent,
            intent.SemanticPlanDigest,
            result.Summary,
            result.Rows,
            result.SourceLabel,
            result.SourceMode,
            result.IsSimulation,
            result.RowCount,
            result.IsTruncated,
            result.QueriedAtUtc));
        state.CloudReadonlyResults.Sort((left, right) =>
            StringComparer.Ordinal.Compare(left.Intent, right.Intent));

        state.CloudReadonlySummary = result.Summary;
        state.CloudReadonlyRows = result.Rows;
        state.CloudReadonlySourceLabel = result.SourceLabel;
        state.CloudReadonlySourcePath = result.SourcePath;
        state.CloudReadonlySourceMode = result.SourceMode;
        state.CloudReadonlyIsSimulation = result.IsSimulation;
        state.CloudReadonlyRowCount = result.RowCount;
        state.CloudReadonlyIsTruncated = result.IsTruncated;
        state.CloudReadonlyQueriedAtUtc = result.QueriedAtUtc;
        state.CloudHealthAssessment = null;
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
