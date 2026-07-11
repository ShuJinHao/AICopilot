using AICopilot.Services.Contracts;
using AICopilot.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;

namespace AICopilot.BackendTests;

internal sealed class CapturingFileStorage(Exception? deleteException = null)
    : IFileStorageService, IPersistenceFileStorageService
{
    private IPersistenceCommitScope? commitScope;

    public int SaveCount { get; private set; }

    public int DeleteCount { get; private set; }

    public int ConfirmCount { get; private set; }

    public int PendingCount { get; private set; }

    public List<string> DeletedPaths { get; } = [];

    public void AttachCommitScope(IPersistenceCommitScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (commitScope is not null && !ReferenceEquals(commitScope, scope))
        {
            throw new InvalidOperationException("A different persistence commit scope is already attached.");
        }

        commitScope = scope;
    }

    public Task<Stream?> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Stream?>(null);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        DeleteCount++;
        DeletedPaths.Add(path);
        if (deleteException is not null)
        {
            throw deleteException;
        }

        return Task.CompletedTask;
    }

    public Task<PersistenceFileStage> StageAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        SaveCount++;
        var commitId = commitScope?.ReserveCommitId() ?? Guid.NewGuid();
        return Task.FromResult(new PersistenceFileStage(commitId, fileName));
    }

    public Task ConfirmBestEffortAsync(
        PersistenceFileStage stage,
        CancellationToken cancellationToken = default)
    {
        ConfirmCount++;
        commitScope?.ReleaseCommitId(stage.CommitId);
        return Task.CompletedTask;
    }

    public async Task RollbackBestEffortAsync(
        PersistenceFileStage stage,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await DeleteAsync(stage.StoragePath, cancellationToken);
        }
        catch when (deleteException is not null)
        {
        }
        finally
        {
            commitScope?.ReleaseCommitId(stage.CommitId);
        }
    }

    public Task LeavePendingAsync(
        PersistenceFileStage stage,
        CancellationToken cancellationToken = default)
    {
        PendingCount++;
        commitScope?.ReleaseCommitId(stage.CommitId);
        return Task.CompletedTask;
    }
}

internal sealed class FixedDocumentFormatPolicy(IReadOnlyCollection<string> supportedExtensions)
    : IDocumentFormatPolicy
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = supportedExtensions;

    public bool IsSupported(string extension)
    {
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed class AlwaysAcquiredPersistenceFileLeaseManager
    : IPersistenceFileReconciliationLeaseManager
{
    public Task<IPersistenceFileReconciliationLease?> TryAcquireAsync(
        Guid commitId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IPersistenceFileReconciliationLease?>(new Lease());
    }

    private sealed class Lease : IPersistenceFileReconciliationLease
    {
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class NeverAcquiredPersistenceFileLeaseManager
    : IPersistenceFileReconciliationLeaseManager
{
    public Task<IPersistenceFileReconciliationLease?> TryAcquireAsync(
        Guid commitId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IPersistenceFileReconciliationLease?>(null);
    }
}

internal sealed class InMemoryPersistenceFileReconciliationJournal
    : IPersistenceFileReconciliationJournal
{
    private readonly Dictionary<Guid, PersistenceFileReconciliationRecord> records = [];

    public Task StageAsync(
        PersistenceFileReconciliationRecord record,
        CancellationToken cancellationToken = default)
    {
        records.Add(record.CommitId, record);
        return Task.CompletedTask;
    }

    public Task<PersistenceFileReconciliationSnapshot> GetPendingAsync(
        int maximumEntries,
        DateTime createdBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        var pending = records.Values
            .Where(record => record.CreatedAtUtc <= createdBeforeUtc)
            .OrderBy(record => record.CreatedAtUtc)
            .Take(maximumEntries)
            .ToArray();
        return Task.FromResult(new PersistenceFileReconciliationSnapshot(pending, false));
    }

    public Task<PersistenceFileReconciliationRecord?> FindByStoragePathAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        var match = records.Values.SingleOrDefault(record =>
            string.Equals(record.StoragePath, storagePath, StringComparison.Ordinal));
        return Task.FromResult(match);
    }

    public Task<bool> ExistsAsync(Guid commitId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(records.ContainsKey(commitId));
    }

    public Task MarkAttemptedAsync(
        Guid commitId,
        DateTime attemptedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task CompleteAsync(Guid commitId, CancellationToken cancellationToken = default)
    {
        records.Remove(commitId);
        return Task.CompletedTask;
    }
}

internal sealed class ExistenceFaultPersistenceFileJournal(
    IPersistenceFileReconciliationJournal inner) : IPersistenceFileReconciliationJournal
{
    public Task StageAsync(
        PersistenceFileReconciliationRecord record,
        CancellationToken cancellationToken = default)
        => inner.StageAsync(record, cancellationToken);

    public Task<PersistenceFileReconciliationSnapshot> GetPendingAsync(
        int maximumEntries,
        DateTime createdBeforeUtc,
        CancellationToken cancellationToken = default)
        => inner.GetPendingAsync(maximumEntries, createdBeforeUtc, cancellationToken);

    public Task<PersistenceFileReconciliationRecord?> FindByStoragePathAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
        => inner.FindByStoragePathAsync(storagePath, cancellationToken);

    public Task<bool> ExistsAsync(Guid commitId, CancellationToken cancellationToken = default)
        => throw new IOException("simulated journal existence failure");

    public Task MarkAttemptedAsync(
        Guid commitId,
        DateTime attemptedAtUtc,
        CancellationToken cancellationToken = default)
        => inner.MarkAttemptedAsync(commitId, attemptedAtUtc, cancellationToken);

    public Task CompleteAsync(Guid commitId, CancellationToken cancellationToken = default)
        => inner.CompleteAsync(commitId, cancellationToken);
}

internal static class PersistenceFileTestStorage
{
    public static LocalFileStorageService CreateStorage(string root)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:RootPath"] = root
            })
            .Build();
        return new LocalFileStorageService(configuration);
    }

    public static string CreateTemporaryRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-persistence-files",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    public static async Task<bool> FileExistsAsync(IFileStorageService storage, string path)
    {
        await using var stream = await storage.GetAsync(path);
        return stream is not null;
    }

    public static async Task<string> CreateStoredFileAsync(
        string root,
        string fileName,
        byte[] contents)
    {
        var relativePath = Path.Combine(
                "uploads",
                "test",
                $"{Guid.NewGuid():N}_{Path.GetFileName(fileName)}")
            .Replace('\\', '/');
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, contents);
        return relativePath;
    }
}
