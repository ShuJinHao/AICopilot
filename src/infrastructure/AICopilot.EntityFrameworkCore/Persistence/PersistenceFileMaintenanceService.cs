using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AICopilot.EntityFrameworkCore.Persistence;

public sealed record PersistenceFileMaintenanceResult(
    int ReconciledCommittedFiles,
    int ReconciledRolledBackFiles,
    int FailedFileReconciliations,
    int SkippedActiveFileReconciliations,
    int DeletedCommitMarkers,
    bool MarkerCleanupSkipped);

public sealed class PersistenceFileMaintenanceService(
    PersistenceCommitMarkerDbContext markerDbContext,
    IPersistenceFileReconciliationJournal journal,
    IPersistenceFileReconciliationLeaseManager leaseManager,
    IFileStorageService fileStorage,
    ILogger<PersistenceFileMaintenanceService> logger,
    IArtifactWorkspaceFileSetStore? artifactFileSetStore = null)
{
    public async Task<PersistenceFileMaintenanceResult> RunOnceAsync(
        DateTime utcNow,
        TimeSpan reconciliationDelay,
        TimeSpan markerRetention,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Maintenance time must be UTC.", nameof(utcNow));
        }

        if (reconciliationDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(reconciliationDelay));
        }

        if (markerRetention <= reconciliationDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(markerRetention),
                "Commit marker retention must be longer than the reconciliation delay.");
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        var snapshot = await journal.GetPendingAsync(
            batchSize,
            utcNow - reconciliationDelay,
            cancellationToken);
        var committedFiles = 0;
        var rolledBackFiles = 0;
        var failedFiles = 0;
        var activeFiles = 0;
        var protectedCommitIds = new HashSet<Guid>();

        foreach (var record in snapshot.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var lease = await leaseManager.TryAcquireAsync(
                record.CommitId,
                cancellationToken);
            if (lease is null)
            {
                activeFiles++;
                protectedCommitIds.Add(record.CommitId);
                await MarkJournalAttemptedAsync(record.CommitId, utcNow, cancellationToken);
                continue;
            }

            var committed = await markerDbContext.CommitMarkers
                .AsNoTracking()
                .AnyAsync(marker => marker.Id == record.CommitId, cancellationToken);
            try
            {
                if (committed)
                {
                    await using var committedFile = await fileStorage.GetAsync(
                        record.StoragePath,
                        cancellationToken);
                    if (committedFile is null)
                    {
                        throw new InvalidOperationException(
                            "A committed persistence file is missing from durable storage.");
                    }

                    await journal.CompleteAsync(record.CommitId, cancellationToken);
                    committedFiles++;
                }
                else
                {
                    await fileStorage.DeleteAsync(record.StoragePath, cancellationToken);
                    await journal.CompleteAsync(record.CommitId, cancellationToken);
                    rolledBackFiles++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failedFiles++;
                protectedCommitIds.Add(record.CommitId);
                logger.LogWarning(
                    "Persistence file reconciliation will be retried. CommitId={CommitId}; ErrorType={ErrorType}",
                    record.CommitId,
                    exception.GetType().Name);
                await MarkJournalAttemptedAsync(record.CommitId, utcNow, cancellationToken);
            }
        }

        var artifactJournalUnreadable = false;
        if (artifactFileSetStore is not null)
        {
            try
            {
                artifactJournalUnreadable = (await artifactFileSetStore.GetPendingAsync(
                    maximumEntries: 1,
                    DateTimeOffset.MaxValue,
                    cancellationToken)).HasUnreadableEntries;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                artifactJournalUnreadable = true;
                logger.LogError(
                    "Persistence commit marker cleanup was blocked because artifact file-set journals could not be inspected. ErrorType={ErrorType}",
                    exception.GetType().Name);
            }
        }

        if (snapshot.HasUnreadableEntries || artifactJournalUnreadable)
        {
            logger.LogError(
                "Persistence commit marker cleanup was skipped because one or more file reconciliation journals are unreadable.");
            return new PersistenceFileMaintenanceResult(
                committedFiles,
                rolledBackFiles,
                failedFiles,
                activeFiles,
                0,
                MarkerCleanupSkipped: true);
        }

        var markerCutoff = utcNow - markerRetention;
        var deletableMarkerIds = new List<Guid>(batchSize);
        var scannedMarkers = 0;
        while (deletableMarkerIds.Count < batchSize)
        {
            var markerCandidates = await markerDbContext.CommitMarkers
                .AsNoTracking()
                .Where(marker => marker.CreatedAtUtc <= markerCutoff)
                .OrderBy(marker => marker.CreatedAtUtc)
                .ThenBy(marker => marker.Id)
                .Select(marker => marker.Id)
                .Skip(scannedMarkers)
                .Take(batchSize)
                .ToArrayAsync(cancellationToken);
            if (markerCandidates.Length == 0)
            {
                break;
            }

            scannedMarkers += markerCandidates.Length;
            foreach (var markerId in markerCandidates)
            {
                if (!protectedCommitIds.Contains(markerId) &&
                    !await journal.ExistsAsync(markerId, cancellationToken) &&
                    (artifactFileSetStore is null ||
                     !await artifactFileSetStore.ExistsPendingAsync(markerId, cancellationToken)))
                {
                    deletableMarkerIds.Add(markerId);
                    if (deletableMarkerIds.Count == batchSize)
                    {
                        break;
                    }
                }
            }
        }

        var deletedMarkers = deletableMarkerIds.Count == 0
            ? 0
            : await markerDbContext.CommitMarkers
                .Where(marker => deletableMarkerIds.Contains(marker.Id))
                .ExecuteDeleteAsync(cancellationToken);

        return new PersistenceFileMaintenanceResult(
            committedFiles,
            rolledBackFiles,
            failedFiles,
            activeFiles,
            deletedMarkers,
            MarkerCleanupSkipped: false);
    }

    private async Task MarkJournalAttemptedAsync(
        Guid commitId,
        DateTime attemptedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await journal.MarkAttemptedAsync(commitId, attemptedAtUtc, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Persistence file reconciliation attempt timestamp update failed. CommitId={CommitId}; ErrorType={ErrorType}",
                commitId,
                exception.GetType().Name);
        }
    }
}
