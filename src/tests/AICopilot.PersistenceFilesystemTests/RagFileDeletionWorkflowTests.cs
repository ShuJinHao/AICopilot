using AICopilot.RagWorker.Consumers;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.PersistenceFilesystemTests;

public sealed class RagFileDeletionWorkflowTests
{
    [Fact]
    public async Task DocumentFileDeletionRequestedConsumer_ShouldDeleteStoredFile()
    {
        var fileStorage = new RecordingFileStorage();
        var journal = new InMemoryPersistenceFileReconciliationJournal();
        var consumer = new DocumentFileDeletionRequestedConsumer(
            fileStorage,
            journal,
            new AlwaysAcquiredPersistenceFileLeaseManager(),
            NullLogger<DocumentFileDeletionRequestedConsumer>.Instance);

        await consumer.DeleteFileAsync(
            new DocumentFileDeletionRequestedEvent
            {
                DocumentId = 123,
                KnowledgeBaseId = Guid.NewGuid(),
                FilePath = "documents/runbook.txt",
                FileName = "runbook.txt"
            },
            CancellationToken.None);

        fileStorage.DeleteCount.Should().Be(1);
        fileStorage.DeletedPaths.Should().ContainSingle().Which.Should().Be("documents/runbook.txt");
    }

    [Fact]
    public async Task DocumentFileDeletionRequestedConsumer_ShouldPropagateDeleteFailures()
    {
        var fileStorage = new RecordingFileStorage(new IOException("delete failed"));
        var consumer = new DocumentFileDeletionRequestedConsumer(
            fileStorage,
            new InMemoryPersistenceFileReconciliationJournal(),
            new AlwaysAcquiredPersistenceFileLeaseManager(),
            NullLogger<DocumentFileDeletionRequestedConsumer>.Instance);

        Func<Task> act = async () => await consumer.DeleteFileAsync(
            new DocumentFileDeletionRequestedEvent
            {
                DocumentId = 123,
                KnowledgeBaseId = Guid.NewGuid(),
                FilePath = "documents/runbook.txt",
                FileName = "runbook.txt"
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<IOException>().WithMessage("delete failed");
        fileStorage.DeleteCount.Should().Be(1);
    }

    [Fact]
    public async Task DocumentFileDeletionRequestedConsumer_ShouldRetirePendingUploadJournalUnderCommitLease()
    {
        var fileStorage = new RecordingFileStorage();
        var journal = new InMemoryPersistenceFileReconciliationJournal();
        var commitId = Guid.NewGuid();
        const string storagePath = "documents/pending-runbook.txt";
        await journal.StageAsync(new PersistenceFileReconciliationRecord(
            commitId,
            storagePath,
            DateTime.UtcNow.AddMinutes(-1)));
        var consumer = new DocumentFileDeletionRequestedConsumer(
            fileStorage,
            journal,
            new AlwaysAcquiredPersistenceFileLeaseManager(),
            NullLogger<DocumentFileDeletionRequestedConsumer>.Instance);

        await consumer.DeleteFileAsync(
            new DocumentFileDeletionRequestedEvent
            {
                DocumentId = 124,
                KnowledgeBaseId = Guid.NewGuid(),
                FilePath = storagePath,
                FileName = "pending-runbook.txt"
            });

        fileStorage.DeletedPaths.Should().ContainSingle().Which.Should().Be(storagePath);
        (await journal.ExistsAsync(commitId)).Should().BeFalse();
    }

    [Fact]
    public async Task DocumentFileDeletionRequestedConsumer_ShouldRetryWhenUploadCommitLeaseIsActive()
    {
        var fileStorage = new RecordingFileStorage();
        var journal = new InMemoryPersistenceFileReconciliationJournal();
        var commitId = Guid.NewGuid();
        const string storagePath = "documents/active-runbook.txt";
        await journal.StageAsync(new PersistenceFileReconciliationRecord(
            commitId,
            storagePath,
            DateTime.UtcNow.AddMinutes(-1)));
        var consumer = new DocumentFileDeletionRequestedConsumer(
            fileStorage,
            journal,
            new NeverAcquiredPersistenceFileLeaseManager(),
            NullLogger<DocumentFileDeletionRequestedConsumer>.Instance);

        Func<Task> action = () => consumer.DeleteFileAsync(
            new DocumentFileDeletionRequestedEvent
            {
                DocumentId = 125,
                KnowledgeBaseId = Guid.NewGuid(),
                FilePath = storagePath,
                FileName = "active-runbook.txt"
            });

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*active persistence commit*");
        fileStorage.DeleteCount.Should().Be(0);
        (await journal.ExistsAsync(commitId)).Should().BeTrue();
    }
    private sealed class RecordingFileStorage(Exception? deleteException = null) : IFileStorageService
    {
        public int DeleteCount { get; private set; }

        public List<string> DeletedPaths { get; } = [];

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
    }

    private sealed class AlwaysAcquiredPersistenceFileLeaseManager
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
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class NeverAcquiredPersistenceFileLeaseManager
        : IPersistenceFileReconciliationLeaseManager
    {
        public Task<IPersistenceFileReconciliationLease?> TryAcquireAsync(
            Guid commitId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IPersistenceFileReconciliationLease?>(null);
        }
    }

    private sealed class InMemoryPersistenceFileReconciliationJournal
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
            return Task.FromResult(records.Values.SingleOrDefault(record =>
                string.Equals(record.StoragePath, storagePath, StringComparison.Ordinal)));
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
}
