using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AICopilot.EntityFrameworkCore.Persistence;

internal sealed class ArtifactFileSetMaintenanceService(
    AiGatewayDbContext aiGatewayDbContext,
    PersistenceCommitMarkerDbContext markerDbContext,
    IArtifactWorkspaceFileSetStore fileSetStore,
    IPersistenceFileReconciliationLeaseManager leaseManager,
    ILogger<ArtifactFileSetMaintenanceService> logger)
    : IArtifactFileSetMaintenanceService
{
    public async Task<ArtifactFileSetMaintenanceResult> RunOnceAsync(
        DateTimeOffset nowUtc,
        TimeSpan reconciliationDelay,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (reconciliationDelay <= TimeSpan.Zero || batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reconciliationDelay));
        }

        var snapshot = await fileSetStore.GetPendingAsync(
            batchSize,
            nowUtc - reconciliationDelay,
            cancellationToken);
        var confirmed = 0;
        var rolledBack = 0;
        var failed = 0;
        var active = 0;
        foreach (var stage in snapshot.Stages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var lease = await leaseManager.TryAcquireAsync(stage.CommitId, cancellationToken);
            if (lease is null)
            {
                active++;
                await MarkAttemptedBestEffortAsync(stage.CommitId, nowUtc, cancellationToken);
                continue;
            }

            try
            {
                var markerExists = await markerDbContext.CommitMarkers
                    .AsNoTracking()
                    .AnyAsync(marker => marker.Id == stage.CommitId, cancellationToken);
                var operation = await aiGatewayDbContext.ArtifactFileSetOperations
                    .AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.CommitId == stage.CommitId, cancellationToken);
                if (markerExists)
                {
                    if (operation is null ||
                        operation.Status != ArtifactFileSetOperationStatus.Completed ||
                        !string.Equals(operation.ManifestDigest, stage.ManifestDigest, StringComparison.Ordinal) ||
                        !string.Equals(operation.PublishedManifestDigest, stage.ManifestDigest, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Committed artifact file-set metadata is missing or conflicts with the staged manifest.");
                    }

                    await fileSetStore.ConfirmPendingAsync(stage, cancellationToken);
                    confirmed++;
                    continue;
                }

                if (operation is not null)
                {
                    throw new InvalidOperationException(
                        "Artifact file-set metadata exists without its retained persistence commit marker.");
                }

                await fileSetStore.RollbackPendingAsync(stage, cancellationToken);
                rolledBack++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed++;
                logger.LogWarning(
                    "Artifact file-set reconciliation will retry. CommitId={CommitId}; ErrorType={ErrorType}",
                    stage.CommitId,
                    exception.GetType().Name);
                await MarkAttemptedBestEffortAsync(stage.CommitId, nowUtc, cancellationToken);
            }
        }

        return new ArtifactFileSetMaintenanceResult(
            confirmed,
            rolledBack,
            failed,
            active,
            snapshot.HasUnreadableEntries);
    }

    private async Task MarkAttemptedBestEffortAsync(
        Guid commitId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await fileSetStore.MarkPendingAttemptedAsync(commitId, nowUtc, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Artifact file-set reconciliation attempt timestamp could not be updated. CommitId={CommitId}; ErrorType={ErrorType}",
                commitId,
                exception.GetType().Name);
        }
    }
}
