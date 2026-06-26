using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentRuntimeCloudReadonlySandboxToolService(
    CloudReadonlySandboxAgentTrialService? cloudSandboxAgentTrialService,
    CloudReadonlySandboxControlledTrialService? cloudSandboxControlledTrialService)
{
    public async Task<object> QueryCloudReadonlySandboxAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        AgentStep step,
        CancellationToken cancellationToken)
    {
        if (plan.IsCloudSandboxControlledTrial || plan.CloudSandboxGoalIntent is not null)
        {
            return await QueryCloudReadonlySandboxControlledAsync(plan, state, step, cancellationToken);
        }

        if (cloudSandboxAgentTrialService is null)
        {
            throw new InvalidOperationException("CloudReadonlySandbox agent trial service is not configured.");
        }

        var scenarioId = AgentRuntimeStepInputReader.ReadString(step.InputJson, "scenarioId") ?? plan.TrialScenarioId;
        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            throw new InvalidOperationException("CloudReadonlySandbox agent trial scenario id is missing.");
        }

        var maxRows = AgentRuntimeStepInputReader.ReadInt(step.InputJson, "maxRows") ?? 20;
        var timeoutMs = AgentRuntimeStepInputReader.ReadInt(step.InputJson, "timeoutMs") ?? 5000;
        var result = await cloudSandboxAgentTrialService.RunScenarioAsync(
            scenarioId,
            plan.ArtifactTypes,
            maxRows,
            timeoutMs,
            cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            throw new InvalidOperationException($"CloudReadonlySandbox agent trial query failed: {AgentRuntimeStepInputReader.BuildResultErrorSummary(result)}");
        }

        var queryResult = result.Value.QueryResult;
        var rows = queryResult.Rows
            .Select(row => row.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        state.CloudReadonlySummary =
            $"CloudReadonly sandbox query executed. sourceType={queryResult.SourceType}; sourceMode={queryResult.SourceMode}; isSandbox={queryResult.IsSandbox.ToString().ToLowerInvariant()}; isSimulation={queryResult.IsSimulation.ToString().ToLowerInvariant()}; sourceLabel={queryResult.SourceLabel}; boundary={queryResult.Boundary}; endpointCode={queryResult.EndpointCode}; queryHash={queryResult.QueryHash}; resultHash={queryResult.ResultHash}; rows={queryResult.RowCount}; truncated={queryResult.IsTruncated.ToString().ToLowerInvariant()}; approvalStatus={queryResult.ApprovalStatus}.";
        state.CloudReadonlyRows = rows;
        state.CloudReadonlySourceLabel = queryResult.SourceLabel;
        state.CloudReadonlySourcePath = queryResult.EndpointCode;
        state.CloudReadonlySourceMode = queryResult.SourceMode;
        state.CloudReadonlyIsSimulation = queryResult.IsSimulation;
        state.CloudReadonlyRowCount = queryResult.RowCount;
        state.CloudReadonlyIsTruncated = queryResult.IsTruncated;
        state.BusinessQueryHash = queryResult.QueryHash;
        state.CloudSandboxQueryResults.Add(new AgentCloudSandboxQuerySummary(
            queryResult.EndpointCode,
            queryResult.SourceMode,
            queryResult.IsSandbox,
            queryResult.SourceLabel,
            queryResult.QueryHash,
            queryResult.ResultHash,
            queryResult.RowCount,
            queryResult.IsTruncated,
            [],
            CloudReadonlySandboxControlledTrialMarkers.FixedScenarioTrialMode,
            null,
            queryResult.Boundary,
            queryResult.ApprovalStatus));

        return new
        {
            status = "completed",
            trialMode = CloudReadonlySandboxControlledTrialMarkers.FixedScenarioTrialMode,
            scenarioId = result.Value.ScenarioId,
            scenarioTitle = result.Value.ScenarioTitle,
            sourceType = queryResult.SourceType,
            sourceMode = queryResult.SourceMode,
            isSandbox = queryResult.IsSandbox,
            isSimulation = queryResult.IsSimulation,
            sourceLabel = queryResult.SourceLabel,
            boundary = queryResult.Boundary,
            endpointCode = queryResult.EndpointCode,
            queryHash = queryResult.QueryHash,
            resultHash = queryResult.ResultHash,
            rowCount = queryResult.RowCount,
            isTruncated = queryResult.IsTruncated,
            approvalStatus = queryResult.ApprovalStatus,
            rows
        };
    }

    public async Task<object> QueryCloudReadonlySandboxControlledAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        AgentStep step,
        CancellationToken cancellationToken)
    {
        if (cloudSandboxControlledTrialService is null)
        {
            throw new InvalidOperationException("CloudReadonlySandbox controlled trial service is not configured.");
        }

        var intent = plan.CloudSandboxGoalIntent
                     ?? throw new InvalidOperationException("CloudReadonlySandbox controlled trial intent is missing.");
        var maxRows = AgentRuntimeStepInputReader.ReadInt(step.InputJson, "maxRows") ?? intent.MaxRows;
        var timeoutMs = AgentRuntimeStepInputReader.ReadInt(step.InputJson, "timeoutMs") ?? 5000;
        var result = await cloudSandboxControlledTrialService.RunIntentAsync(
            intent,
            plan.ArtifactTypes,
            maxRows,
            timeoutMs,
            cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            throw new InvalidOperationException($"CloudReadonlySandbox controlled trial query failed: {AgentRuntimeStepInputReader.BuildResultErrorSummary(result)}");
        }

        var queryResult = result.Value.QueryResult;
        var rows = queryResult.Rows
            .Select(row => row.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        state.CloudReadonlySummary =
            $"CloudReadonly sandbox controlled query executed. trialMode={CloudReadonlySandboxControlledTrialMarkers.TrialMode}; intentId={intent.IntentId}; sourceType={queryResult.SourceType}; sourceMode={queryResult.SourceMode}; isSandbox={queryResult.IsSandbox.ToString().ToLowerInvariant()}; isSimulation={queryResult.IsSimulation.ToString().ToLowerInvariant()}; sourceLabel={queryResult.SourceLabel}; boundary={queryResult.Boundary}; endpointCode={queryResult.EndpointCode}; queryHash={queryResult.QueryHash}; resultHash={queryResult.ResultHash}; rows={queryResult.RowCount}; truncated={queryResult.IsTruncated.ToString().ToLowerInvariant()}; approvalStatus={queryResult.ApprovalStatus}.";
        state.CloudReadonlyRows = rows;
        state.CloudReadonlySourceLabel = queryResult.SourceLabel;
        state.CloudReadonlySourcePath = queryResult.EndpointCode;
        state.CloudReadonlySourceMode = queryResult.SourceMode;
        state.CloudReadonlyIsSimulation = queryResult.IsSimulation;
        state.CloudReadonlyRowCount = queryResult.RowCount;
        state.CloudReadonlyIsTruncated = queryResult.IsTruncated;
        state.BusinessQueryHash = queryResult.QueryHash;
        state.CloudSandboxQueryResults.Add(new AgentCloudSandboxQuerySummary(
            queryResult.EndpointCode,
            queryResult.SourceMode,
            queryResult.IsSandbox,
            queryResult.SourceLabel,
            queryResult.QueryHash,
            queryResult.ResultHash,
            queryResult.RowCount,
            queryResult.IsTruncated,
            [],
            CloudReadonlySandboxControlledTrialMarkers.TrialMode,
            intent.IntentId,
            queryResult.Boundary,
            queryResult.ApprovalStatus));

        return new
        {
            status = "completed",
            trialMode = CloudReadonlySandboxControlledTrialMarkers.TrialMode,
            intentId = intent.IntentId,
            analysisType = intent.AnalysisType,
            sourceType = queryResult.SourceType,
            sourceMode = queryResult.SourceMode,
            isSandbox = queryResult.IsSandbox,
            isSimulation = queryResult.IsSimulation,
            sourceLabel = queryResult.SourceLabel,
            boundary = queryResult.Boundary,
            endpointCode = queryResult.EndpointCode,
            queryHash = queryResult.QueryHash,
            resultHash = queryResult.ResultHash,
            rowCount = queryResult.RowCount,
            isTruncated = queryResult.IsTruncated,
            approvalStatus = queryResult.ApprovalStatus,
            rows
        };
    }
}
