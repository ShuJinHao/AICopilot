using AICopilot.Core.AiGateway.Aggregates.ProductionOperations;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.CloudReadiness;

internal sealed class InMemoryCloudReadonlyProductionControlledPilotStore
    : ICloudReadonlyProductionControlledPilotStore
{
    private readonly object sync = new();
    private readonly Dictionary<string, CloudProductionGoalIntentDto> intents = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CloudReadonlyProductionControlledPilotResultDto> runs = [];

    public void SaveIntent(CloudProductionGoalIntentDto intent)
    {
        lock (sync)
        {
            intents[intent.IntentId] = intent;
            foreach (var key in intents.Keys.Take(Math.Max(0, intents.Count - 100)).ToArray())
            {
                intents.Remove(key);
            }
        }
    }

    public CloudProductionGoalIntentDto? GetIntent(string intentId)
    {
        lock (sync)
        {
            return string.IsNullOrWhiteSpace(intentId) ? null : intents.GetValueOrDefault(intentId);
        }
    }

    public void SaveRun(CloudReadonlyProductionControlledPilotResultDto result)
    {
        lock (sync)
        {
            runs.Insert(0, result);
            if (runs.Count > 20)
            {
                runs.RemoveRange(20, runs.Count - 20);
            }
        }
    }

    public IReadOnlyCollection<CloudReadonlyProductionControlledPilotResultDto> ListRuns()
    {
        lock (sync)
        {
            return runs.ToArray();
        }
    }
}

internal sealed class RepositoryCloudReadonlyProductionControlledPilotStore(
    IRepository<ProductionControlledPilotIntent> intentRepository,
    IRepository<ProductionControlledPilotRun> runRepository)
    : RepositoryCloudReadinessStoreBase, ICloudReadonlyProductionControlledPilotStore
{
    public void SaveIntent(CloudProductionGoalIntentDto intent)
    {
        Execute(async () =>
        {
            var existing = (await intentRepository.GetListAsync(
                item => item.IntentId == intent.IntentId,
                CancellationToken.None)).FirstOrDefault();
            if (existing is null)
            {
                intentRepository.Add(new ProductionControlledPilotIntent(
                    intent.IntentId,
                    intent.GoalHash,
                    intent.EndpointCodes,
                    intent.DeviceId,
                    intent.PassStationTypeKey,
                    intent.TimeRange.From,
                    intent.TimeRange.To,
                    intent.MaxRows,
                    intent.ArtifactTypes,
                    intent.AnalysisType,
                    intent.Warnings,
                    intent.RejectedReasons,
                    intent.RequiresToolApproval,
                    intent.RequiresFinalApproval,
                    DateTimeOffset.UtcNow));
            }
            else
            {
                existing.Update(
                    intent.IntentId,
                    intent.GoalHash,
                    intent.EndpointCodes,
                    intent.DeviceId,
                    intent.PassStationTypeKey,
                    intent.TimeRange.From,
                    intent.TimeRange.To,
                    intent.MaxRows,
                    intent.ArtifactTypes,
                    intent.AnalysisType,
                    intent.Warnings,
                    intent.RejectedReasons,
                    intent.RequiresToolApproval,
                    intent.RequiresFinalApproval,
                    DateTimeOffset.UtcNow);
            }

            await intentRepository.SaveChangesAsync();
        });
    }

    public CloudProductionGoalIntentDto? GetIntent(string intentId)
    {
        if (string.IsNullOrWhiteSpace(intentId))
        {
            return null;
        }

        return Execute(async () =>
            (await intentRepository.GetListAsync(
                item => item.IntentId == intentId,
                CancellationToken.None)).Select(ToDto).FirstOrDefault());
    }

    public void SaveRun(CloudReadonlyProductionControlledPilotResultDto result)
    {
        Execute(async () =>
        {
            var runId = RunIdFor(result);
            var existing = (await runRepository.GetListAsync(
                item => item.RunId == runId,
                CancellationToken.None)).FirstOrDefault();
            var query = result.QueryResult;
            if (existing is null)
            {
                runRepository.Add(new ProductionControlledPilotRun(
                    runId,
                    result.IntentId,
                    result.AnalysisType,
                    result.Status,
                    query.EndpointCode,
                    query.SourceType,
                    query.SourceMode,
                    query.IsProductionData,
                    query.IsSandbox,
                    query.IsSimulation,
                    query.SourceLabel,
                    query.Boundary,
                    query.PilotWindowId,
                    query.QueryHash,
                    query.ResultHash,
                    query.RowCount,
                    query.IsTruncated,
                    query.ExecutedAt,
                    query.DurationMs,
                    query.ApprovalStatus,
                    result.ArtifactTypes,
                    DateTimeOffset.UtcNow));
            }
            else
            {
                existing.Update(
                    runId,
                    result.IntentId,
                    result.AnalysisType,
                    result.Status,
                    query.EndpointCode,
                    query.SourceType,
                    query.SourceMode,
                    query.IsProductionData,
                    query.IsSandbox,
                    query.IsSimulation,
                    query.SourceLabel,
                    query.Boundary,
                    query.PilotWindowId,
                    query.QueryHash,
                    query.ResultHash,
                    query.RowCount,
                    query.IsTruncated,
                    query.ExecutedAt,
                    query.DurationMs,
                    query.ApprovalStatus,
                    result.ArtifactTypes,
                    DateTimeOffset.UtcNow);
            }

            await runRepository.SaveChangesAsync();
        });
    }

    public IReadOnlyCollection<CloudReadonlyProductionControlledPilotResultDto> ListRuns()
    {
        return Execute(async () =>
            (await runRepository.ListAsync())
            .OrderByDescending(item => item.ExecutedAt)
            .Take(20)
            .Select(ToDto)
            .ToArray());
    }

    private static CloudProductionGoalIntentDto ToDto(ProductionControlledPilotIntent intent) =>
        new(
            intent.IntentId,
            intent.GoalHash,
            intent.EndpointCodes,
            intent.DeviceId,
            intent.PassStationTypeKey,
            new CloudProductionGoalTimeRangeDto(intent.TimeRangeFrom, intent.TimeRangeTo),
            intent.MaxRows,
            intent.ArtifactTypes,
            intent.AnalysisType,
            intent.Warnings,
            intent.RejectedReasons,
            intent.RequiresToolApproval,
            intent.RequiresFinalApproval);

    private static CloudReadonlyProductionControlledPilotResultDto ToDto(ProductionControlledPilotRun run) =>
        new(
            run.IntentId,
            run.AnalysisType,
            run.Status,
            new CloudProductionControlledQueryResultDto(
                run.EndpointCode,
                run.SourceType,
                run.SourceMode,
                run.IsProductionData,
                run.IsSandbox,
                run.IsSimulation,
                run.SourceLabel,
                run.Boundary,
                run.PilotWindowId,
                run.IntentId,
                run.QueryHash,
                run.ResultHash,
                run.RowCount,
                run.IsTruncated,
                Rows: [],
                run.ExecutedAt,
                run.DurationMs,
                run.ApprovalStatus),
            run.ArtifactTypes,
            run.Boundary);

    private static string RunIdFor(CloudReadonlyProductionControlledPilotResultDto result)
    {
        var query = result.QueryResult;
        var hash = query.ResultHash.Length <= 16 ? query.ResultHash : query.ResultHash[..16];
        return $"p13_{hash}_{query.ExecutedAt.UtcTicks}";
    }

}
