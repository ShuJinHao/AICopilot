using AICopilot.Core.AiGateway.Aggregates.ProductionOperations;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.CloudReadiness;

internal sealed class InMemoryCloudReadonlyProductionPilotStore : ICloudReadonlyProductionPilotStore
{
    private readonly object sync = new();
    private readonly Dictionary<string, CloudReadonlyProductionPilotWindowDto> windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CloudReadonlyProductionPilotScenarioResultDto> runs = [];

    public void SaveWindow(CloudReadonlyProductionPilotWindowDto window)
    {
        lock (sync)
        {
            windows[window.WindowId] = window;
        }
    }

    public CloudReadonlyProductionPilotWindowDto? GetWindow(string windowId)
    {
        lock (sync)
        {
            return string.IsNullOrWhiteSpace(windowId) ? null : windows.GetValueOrDefault(windowId);
        }
    }

    public CloudReadonlyProductionPilotWindowDto? LatestWindow()
    {
        lock (sync)
        {
            return windows.Values.LastOrDefault();
        }
    }

    public void SaveRun(CloudReadonlyProductionPilotScenarioResultDto result)
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

    public IReadOnlyCollection<CloudReadonlyProductionPilotScenarioResultDto> ListRuns()
    {
        lock (sync)
        {
            return runs.ToArray();
        }
    }
}

internal sealed class RepositoryCloudReadonlyProductionPilotStore(
    IRepository<ProductionPilotWindow> windowRepository,
    IRepository<ProductionPilotRun> runRepository)
    : RepositoryCloudReadinessStoreBase, ICloudReadonlyProductionPilotStore
{
    public void SaveWindow(CloudReadonlyProductionPilotWindowDto window)
    {
        Execute(async () =>
        {
            var existing = (await windowRepository.GetListAsync(
                item => item.WindowId == window.WindowId,
                CancellationToken.None)).FirstOrDefault();
            if (existing is null)
            {
                windowRepository.Add(new ProductionPilotWindow(
                    window.WindowId,
                    window.Name,
                    window.Status,
                    window.StartAt,
                    window.EndAt,
                    window.AllowedEndpointCodes,
                    window.MaxTimeRangeDays,
                    window.MaxRows,
                    window.TimeoutMs,
                    window.OwnerDepartment,
                    window.ApprovalPolicy,
                    window.RollbackPolicy,
                    DateTimeOffset.UtcNow));
            }
            else
            {
                existing.Update(
                    window.WindowId,
                    window.Name,
                    window.Status,
                    window.StartAt,
                    window.EndAt,
                    window.AllowedEndpointCodes,
                    window.MaxTimeRangeDays,
                    window.MaxRows,
                    window.TimeoutMs,
                    window.OwnerDepartment,
                    window.ApprovalPolicy,
                    window.RollbackPolicy,
                    DateTimeOffset.UtcNow);
            }

            await windowRepository.SaveChangesAsync();
        });
    }

    public CloudReadonlyProductionPilotWindowDto? GetWindow(string windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
        {
            return null;
        }

        return Execute(async () =>
            (await windowRepository.GetListAsync(
                item => item.WindowId == windowId,
                CancellationToken.None)).Select(ToDto).FirstOrDefault());
    }

    public CloudReadonlyProductionPilotWindowDto? LatestWindow()
    {
        return Execute(async () =>
            (await windowRepository.ListAsync())
            .OrderByDescending(item => item.CreatedAt)
            .Select(ToDto)
            .FirstOrDefault());
    }

    public void SaveRun(CloudReadonlyProductionPilotScenarioResultDto result)
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
                runRepository.Add(new ProductionPilotRun(
                    runId,
                    result.ScenarioId,
                    result.ScenarioTitle,
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
                    result.ScenarioId,
                    result.ScenarioTitle,
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

    public IReadOnlyCollection<CloudReadonlyProductionPilotScenarioResultDto> ListRuns()
    {
        return Execute(async () =>
            (await runRepository.ListAsync())
            .OrderByDescending(item => item.ExecutedAt)
            .Take(20)
            .Select(ToDto)
            .ToArray());
    }

    private static CloudReadonlyProductionPilotWindowDto ToDto(ProductionPilotWindow window) =>
        new(
            window.WindowId,
            window.Name,
            window.Status,
            window.StartAt,
            window.EndAt,
            window.AllowedEndpointCodes,
            window.MaxTimeRangeDays,
            window.MaxRows,
            window.TimeoutMs,
            window.OwnerDepartment,
            window.ApprovalPolicy,
            window.RollbackPolicy);

    private static CloudReadonlyProductionPilotScenarioResultDto ToDto(ProductionPilotRun run) =>
        new(
            run.ScenarioId,
            run.ScenarioTitle,
            run.Status,
            new CloudProductionPilotQueryResultDto(
                run.EndpointCode,
                run.SourceType,
                run.SourceMode,
                run.IsProductionData,
                run.IsSandbox,
                run.IsSimulation,
                run.SourceLabel,
                run.Boundary,
                run.PilotWindowId,
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

    private static string RunIdFor(CloudReadonlyProductionPilotScenarioResultDto result)
    {
        var query = result.QueryResult;
        var hash = query.QueryHash.Length <= 16 ? query.QueryHash : query.QueryHash[..16];
        return $"p12_{hash}_{query.ExecutedAt.UtcTicks}";
    }

}
