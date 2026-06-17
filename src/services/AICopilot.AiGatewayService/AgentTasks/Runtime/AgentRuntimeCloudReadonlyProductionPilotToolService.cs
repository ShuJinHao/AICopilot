using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentRuntimeCloudReadonlyProductionPilotToolService(
    CloudReadonlyProductionPilotService? cloudReadonlyProductionPilotService,
    CloudReadonlyProductionControlledPilotService? cloudReadonlyProductionControlledPilotService,
    CloudReadonlyPilotReadinessService? cloudReadonlyPilotReadinessService,
    IReadRepository<ToolRegistration>? toolReadRepository)
{
    public async Task<object> QueryCloudReadonlyProductionPilotAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        AgentStep step,
        CancellationToken cancellationToken)
    {
        if (cloudReadonlyProductionPilotService is null ||
            cloudReadonlyPilotReadinessService is null ||
            toolReadRepository is null)
        {
            throw new InvalidOperationException("CloudReadonlyProductionPilot service is not configured.");
        }

        if (!plan.IsCloudProductionPilotTrial)
        {
            throw new InvalidOperationException("CloudReadonlyProductionPilot tool is only allowed inside P12 production Pilot plans.");
        }

        var scenarioId = AgentRuntimeStepInputReader.ReadString(step.InputJson, "scenarioId") ?? plan.TrialScenarioId;
        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            throw new InvalidOperationException("CloudReadonlyProductionPilot scenario id is missing.");
        }

        var maxRows = AgentRuntimeStepInputReader.ReadInt(step.InputJson, "maxRows") ?? 20;
        var timeoutMs = AgentRuntimeStepInputReader.ReadInt(step.InputJson, "timeoutMs") ?? 5000;
        var windowId = AgentRuntimeStepInputReader.ReadString(step.InputJson, "pilotWindowId");
        var deviceId = Guid.TryParse(AgentRuntimeStepInputReader.ReadString(step.InputJson, "deviceId"), out var parsedDeviceId)
            ? parsedDeviceId
            : (Guid?)null;
        var passStationTypeKey = AgentRuntimeStepInputReader.ReadString(step.InputJson, "passStationTypeKey");
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolReadRepository,
            cancellationToken);
        var result = await cloudReadonlyProductionPilotService.RunScenarioAsync(
            new RunCloudReadonlyProductionPilotScenarioCommand(
                scenarioId,
                plan.ArtifactTypes,
                windowId,
                TimeRange: null,
                DeviceId: deviceId,
                PassStationTypeKey: passStationTypeKey,
                MaxRows: maxRows,
                TimeoutMs: timeoutMs),
            cloudReadonlyPilotReadinessService.BuildStatus(protectedTools),
            protectedTools,
            cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            throw new InvalidOperationException($"CloudReadonlyProductionPilot query failed: {AgentRuntimeStepInputReader.BuildResultErrorSummary(result)}");
        }

        var queryResult = result.Value.QueryResult;
        var rows = queryResult.Rows
            .Select(row => row.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        state.CloudReadonlySummary =
            $"CloudReadonly production Pilot query executed. sourceType={queryResult.SourceType}; sourceMode={queryResult.SourceMode}; isProductionData={queryResult.IsProductionData.ToString().ToLowerInvariant()}; isSandbox={queryResult.IsSandbox.ToString().ToLowerInvariant()}; isSimulation={queryResult.IsSimulation.ToString().ToLowerInvariant()}; sourceLabel={queryResult.SourceLabel}; boundary={queryResult.Boundary}; pilotWindowId={queryResult.PilotWindowId}; endpointCode={queryResult.EndpointCode}; queryHash={queryResult.QueryHash}; resultHash={queryResult.ResultHash}; rows={queryResult.RowCount}; truncated={queryResult.IsTruncated.ToString().ToLowerInvariant()}; approvalStatus={queryResult.ApprovalStatus}.";
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
            "ProductionPilotFixedScenario",
            null,
            queryResult.Boundary,
            queryResult.ApprovalStatus));

        return new
        {
            status = "completed",
            trialMode = "ProductionPilotFixedScenario",
            scenarioId = result.Value.ScenarioId,
            scenarioTitle = result.Value.ScenarioTitle,
            sourceType = queryResult.SourceType,
            sourceMode = queryResult.SourceMode,
            isProductionData = queryResult.IsProductionData,
            isSandbox = queryResult.IsSandbox,
            isSimulation = queryResult.IsSimulation,
            sourceLabel = queryResult.SourceLabel,
            boundary = queryResult.Boundary,
            pilotWindowId = queryResult.PilotWindowId,
            endpointCode = queryResult.EndpointCode,
            queryHash = queryResult.QueryHash,
            resultHash = queryResult.ResultHash,
            rowCount = queryResult.RowCount,
            isTruncated = queryResult.IsTruncated,
            approvalStatus = queryResult.ApprovalStatus,
            rows
        };
    }

    public async Task<object> QueryCloudReadonlyProductionControlledPilotAsync(
        AgentTaskPlanDocument plan,
        AgentTaskRunState state,
        AgentStep step,
        CancellationToken cancellationToken)
    {
        if (cloudReadonlyProductionControlledPilotService is null ||
            cloudReadonlyProductionPilotService is null ||
            cloudReadonlyPilotReadinessService is null ||
            toolReadRepository is null)
        {
            throw new InvalidOperationException("CloudReadonlyProductionControlledPilot service is not configured.");
        }

        if (!plan.IsCloudProductionControlledPilotTrial)
        {
            throw new InvalidOperationException("CloudReadonlyProductionControlledPilot tool is only allowed inside P13 controlled production Pilot plans.");
        }

        var intentId = AgentRuntimeStepInputReader.ReadString(step.InputJson, "intentId") ?? plan.CloudProductionGoalIntent?.IntentId;
        if (string.IsNullOrWhiteSpace(intentId))
        {
            throw new InvalidOperationException("CloudProductionGoalIntent id is missing.");
        }

        var maxRows = AgentRuntimeStepInputReader.ReadInt(step.InputJson, "maxRows") ?? plan.CloudProductionGoalIntent?.MaxRows ?? 20;
        var timeoutMs = AgentRuntimeStepInputReader.ReadInt(step.InputJson, "timeoutMs") ?? 5000;
        var protectedTools = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
            toolReadRepository,
            cancellationToken);
        var p12Status = cloudReadonlyProductionPilotService.BuildStatus(
            cloudReadonlyPilotReadinessService.BuildStatus(protectedTools),
            protectedTools);
        var result = await cloudReadonlyProductionControlledPilotService.RunIntentAsync(
            intentId,
            plan.ArtifactTypes,
            maxRows,
            timeoutMs,
            p12Status,
            protectedTools,
            cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            throw new InvalidOperationException($"CloudReadonlyProductionControlledPilot query failed: {AgentRuntimeStepInputReader.BuildResultErrorSummary(result)}");
        }

        var queryResult = result.Value.QueryResult;
        var rows = queryResult.Rows
            .Select(row => row.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        state.CloudReadonlySummary =
            $"CloudReadonly production controlled Pilot query executed. sourceType={queryResult.SourceType}; sourceMode={queryResult.SourceMode}; isProductionData={queryResult.IsProductionData.ToString().ToLowerInvariant()}; isSandbox={queryResult.IsSandbox.ToString().ToLowerInvariant()}; isSimulation={queryResult.IsSimulation.ToString().ToLowerInvariant()}; sourceLabel={queryResult.SourceLabel}; boundary={queryResult.Boundary}; pilotWindowId={queryResult.PilotWindowId}; intentId={queryResult.IntentId}; endpointCode={queryResult.EndpointCode}; queryHash={queryResult.QueryHash}; resultHash={queryResult.ResultHash}; rows={queryResult.RowCount}; truncated={queryResult.IsTruncated.ToString().ToLowerInvariant()}; approvalStatus={queryResult.ApprovalStatus}.";
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
            CloudReadonlyProductionControlledPilotMarkers.TrialMode,
            queryResult.IntentId,
            queryResult.Boundary,
            queryResult.ApprovalStatus));

        return new
        {
            status = "completed",
            trialMode = CloudReadonlyProductionControlledPilotMarkers.TrialMode,
            intentId = result.Value.IntentId,
            analysisType = result.Value.AnalysisType,
            sourceType = queryResult.SourceType,
            sourceMode = queryResult.SourceMode,
            isProductionData = queryResult.IsProductionData,
            isSandbox = queryResult.IsSandbox,
            isSimulation = queryResult.IsSimulation,
            sourceLabel = queryResult.SourceLabel,
            boundary = queryResult.Boundary,
            pilotWindowId = queryResult.PilotWindowId,
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
