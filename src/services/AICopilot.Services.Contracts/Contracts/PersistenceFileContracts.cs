namespace AICopilot.Services.Contracts;

public interface IPersistenceCommitScope
{
    Guid? CurrentCommitId { get; }

    Guid ReserveCommitId();

    void ReleaseCommitId(Guid commitId);
}

public sealed record PersistenceFileStage(Guid CommitId, string StoragePath);

public interface IPersistenceFileStorageService
{
    Task<PersistenceFileStage> StageAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default);

    Task ConfirmBestEffortAsync(
        PersistenceFileStage stage,
        CancellationToken cancellationToken = default);

    Task RollbackBestEffortAsync(
        PersistenceFileStage stage,
        CancellationToken cancellationToken = default);

    Task LeavePendingAsync(
        PersistenceFileStage stage,
        CancellationToken cancellationToken = default);
}

public static class PersistenceFileCommitProtocol
{
    public static async Task<TResult> ExecuteAsync<TResult>(
        this IPersistenceFileStorageService storage,
        PersistenceFileStage? stage,
        Func<CancellationToken, Task<TResult>> persistDatabaseAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(persistDatabaseAsync);

        TResult result;
        try
        {
            result = await persistDatabaseAsync(cancellationToken);
        }
        catch (PersistenceCommitOutcomeUnknownException)
        {
            if (stage is not null)
            {
                await storage.LeavePendingAsync(stage, CancellationToken.None);
            }

            throw;
        }
        catch
        {
            if (stage is not null)
            {
                await storage.RollbackBestEffortAsync(stage, CancellationToken.None);
            }

            throw;
        }

        if (stage is not null)
        {
            await storage.ConfirmBestEffortAsync(stage, CancellationToken.None);
        }

        return result;
    }
}

public sealed record PersistenceFileReconciliationRecord(
    Guid CommitId,
    string StoragePath,
    DateTime CreatedAtUtc);

public sealed record PersistenceFileReconciliationSnapshot(
    IReadOnlyList<PersistenceFileReconciliationRecord> Records,
    bool HasUnreadableEntries);

public interface IPersistenceFileReconciliationLease : IAsyncDisposable
{
}

public interface IPersistenceFileReconciliationLeaseManager
{
    Task<IPersistenceFileReconciliationLease?> TryAcquireAsync(
        Guid commitId,
        CancellationToken cancellationToken = default);
}

public interface IPersistenceFileReconciliationJournal
{
    Task StageAsync(
        PersistenceFileReconciliationRecord record,
        CancellationToken cancellationToken = default);

    Task<PersistenceFileReconciliationSnapshot> GetPendingAsync(
        int maximumEntries,
        DateTime createdBeforeUtc,
        CancellationToken cancellationToken = default);

    Task<PersistenceFileReconciliationRecord?> FindByStoragePathAsync(
        string storagePath,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Guid commitId,
        CancellationToken cancellationToken = default);

    Task MarkAttemptedAsync(
        Guid commitId,
        DateTime attemptedAtUtc,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        Guid commitId,
        CancellationToken cancellationToken = default);
}
