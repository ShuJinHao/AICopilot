using AICopilot.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace AICopilot.Infrastructure.Storage;

public sealed class LocalPersistenceFileStorageService(
    LocalFileStorageService fileStorage,
    IPersistenceFileReconciliationJournal journal,
    IPersistenceFileReconciliationLeaseManager leaseManager,
    IPersistenceCommitScope commitScope,
    ILogger<LocalPersistenceFileStorageService> logger) : IPersistenceFileStorageService
{
    private IPersistenceFileReconciliationLease? activeLease;
    private PersistenceFileStage? activeStage;

    public async Task<PersistenceFileStage> StageAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (activeLease is not null)
        {
            throw new InvalidOperationException(
                "The current service scope already has a pending persistence file stage.");
        }

        var storagePath = fileStorage.CreateUploadPath(fileName);
        var commitId = commitScope.ReserveCommitId();
        var stage = new PersistenceFileStage(commitId, storagePath);
        var journalStaged = false;
        try
        {
            activeLease = await leaseManager.TryAcquireAsync(commitId, cancellationToken)
                          ?? throw new InvalidOperationException(
                              "Unable to acquire the persistence file reconciliation lease.");
            activeStage = stage;
            await journal.StageAsync(
                new PersistenceFileReconciliationRecord(
                    commitId,
                    storagePath,
                    DateTime.UtcNow),
                cancellationToken);
            journalStaged = true;
            await fileStorage.SaveAtAsync(stream, storagePath, cancellationToken);
            return stage;
        }
        catch
        {
            if (journalStaged)
            {
                await RollbackBestEffortAsync(stage, CancellationToken.None);
            }
            else
            {
                commitScope.ReleaseCommitId(commitId);
                await ReleaseLeaseAsync(commitId);
            }

            throw;
        }
    }

    public async Task ConfirmBestEffortAsync(
        PersistenceFileStage stage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stage);
        EnsureActiveStage(stage);
        if (commitScope.CurrentCommitId == stage.CommitId)
        {
            await RollbackBestEffortAsync(stage, CancellationToken.None);
            throw new InvalidOperationException(
                "A persistence file cannot be confirmed before database persistence consumes its commit id.");
        }

        try
        {
            await journal.CompleteAsync(stage.CommitId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "Persistence file confirmation journal cleanup failed. CommitId={CommitId}; ErrorType={ErrorType}",
                stage.CommitId,
                exception.GetType().Name);
        }
        finally
        {
            try
            {
                commitScope.ReleaseCommitId(stage.CommitId);
            }
            finally
            {
                await ReleaseLeaseAsync(stage.CommitId);
            }
        }
    }

    public async Task RollbackBestEffortAsync(
        PersistenceFileStage stage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stage);
        EnsureActiveStage(stage);
        try
        {
            await fileStorage.DeleteAsync(stage.StoragePath, cancellationToken);
            await journal.CompleteAsync(stage.CommitId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "Persistence file rollback remains pending for background reconciliation. CommitId={CommitId}; ErrorType={ErrorType}",
                stage.CommitId,
                exception.GetType().Name);
        }
        finally
        {
            try
            {
                commitScope.ReleaseCommitId(stage.CommitId);
            }
            finally
            {
                await ReleaseLeaseAsync(stage.CommitId);
            }
        }
    }

    public async Task LeavePendingAsync(
        PersistenceFileStage stage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stage);
        EnsureActiveStage(stage);
        try
        {
            commitScope.ReleaseCommitId(stage.CommitId);
        }
        finally
        {
            await ReleaseLeaseAsync(stage.CommitId);
        }
    }

    private void EnsureActiveStage(PersistenceFileStage stage)
    {
        if (activeLease is null || activeStage != stage)
        {
            throw new InvalidOperationException(
                "The persistence file stage does not match the active reconciliation lease.");
        }
    }

    private async Task ReleaseLeaseAsync(Guid commitId)
    {
        var lease = activeLease;
        activeStage = null;
        activeLease = null;
        await PersistenceReconciliationLeaseDisposer.DisposeBestEffortAsync(
            lease,
            commitId,
            "PersistenceFile",
            logger);
    }
}
