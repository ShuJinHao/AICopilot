using AICopilot.Infrastructure.Storage;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Configuration;

namespace AICopilot.PersistenceTestKit;

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
