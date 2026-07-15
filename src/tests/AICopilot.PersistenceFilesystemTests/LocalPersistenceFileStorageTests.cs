using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Infrastructure.Storage;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static AICopilot.PersistenceTestKit.PersistenceFileTestStorage;

namespace AICopilot.PersistenceFilesystemTests;

public sealed class LocalPersistenceFileStorageTests
{
    [Fact]
    public async Task LocalStorage_ShouldRejectParentTraversalEvenWhenSiblingDiffersOnlyByCase()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-persistence-paths",
            Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "StorageRoot");
        Directory.CreateDirectory(root);
        try
        {
            var storage = CreateStorage(root);
            Func<Task> action = () => storage.DeleteAsync("../storageroot/victim.txt");

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*parent traversal*");
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task LocalStorage_ShouldRejectSymbolicLinkTraversalOutsideConfiguredRoot()
    {
        var parent = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-persistence-links",
            Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "storage");
        var outside = Path.Combine(parent, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        var victim = Path.Combine(outside, "victim.txt");
        await File.WriteAllTextAsync(victim, "protected");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, "uploads"), outside);
            var storage = CreateStorage(root);

            Func<Task> action = () => storage.DeleteAsync("uploads/victim.txt");

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*symbolic links or reparse points*");
            File.Exists(victim).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public async Task LocalPersistenceStorage_ShouldStripWindowsClientPathOnUnixHosts()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var storage = CreateStorage(root);
            var persistentStorage = new LocalPersistenceFileStorageService(
                storage,
                storage,
                new AlwaysAcquiredPersistenceFileLeaseManager(),
                new PersistenceCommitScope(),
                NullLogger<LocalPersistenceFileStorageService>.Instance);

            var stage = await persistentStorage.StageAsync(
                new MemoryStream([1]),
                @"C:\Users\alice\secret.txt");

            stage.StoragePath.Should().EndWith("_secret.txt");
            stage.StoragePath.Should().NotContain("alice");
            stage.StoragePath.Should().NotContain("\\");
            await persistentStorage.RollbackBestEffortAsync(stage);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LocalStorage_ShouldReapplyDirectoryBarrier_WhenDeleteSucceededBeforeFsyncFailure()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTemporaryRoot();
        var storagePath = await CreateStoredFileAsync(root, "barrier.txt", [1]);
        var fullPath = Path.Combine(root, storagePath.Replace('/', Path.DirectorySeparatorChar));
        var parent = Path.GetDirectoryName(fullPath)!;
        var originalMode = File.GetUnixFileMode(parent);
        try
        {
            File.SetUnixFileMode(parent, UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            Func<Task> firstDelete = () => CreateStorage(root).DeleteAsync(storagePath);

            await firstDelete.Should().ThrowAsync<IOException>();
            File.Exists(fullPath).Should().BeFalse();

            File.SetUnixFileMode(parent, originalMode);
            await CreateStorage(root).DeleteAsync(storagePath);
        }
        finally
        {
            File.SetUnixFileMode(parent, originalMode);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LocalJournal_ShouldProveDurableAbsence_AfterDeleteFsyncFailure()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTemporaryRoot();
        var storage = CreateStorage(root);
        var commitId = Guid.NewGuid();
        await storage.StageAsync(new PersistenceFileReconciliationRecord(
            commitId,
            "uploads/barrier.txt",
            DateTime.UtcNow));
        var journalDirectory = Path.Combine(root, ".persistence", "file-reconciliation");
        var journalPath = Path.Combine(journalDirectory, $"{commitId:N}.json");
        var originalMode = File.GetUnixFileMode(journalDirectory);
        try
        {
            File.SetUnixFileMode(
                journalDirectory,
                UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            Func<Task> firstComplete = () => storage.CompleteAsync(commitId);

            await firstComplete.Should().ThrowAsync<IOException>();
            File.Exists(journalPath).Should().BeFalse();

            File.SetUnixFileMode(journalDirectory, originalMode);
            (await storage.ExistsAsync(commitId)).Should().BeFalse();
            (await storage.FindByStoragePathAsync("uploads/barrier.txt")).Should().BeNull();
        }
        finally
        {
            File.SetUnixFileMode(journalDirectory, originalMode);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LocalPersistenceStorage_ShouldKeepJournalUntilCommitOrRollbackIsConfirmed()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var storage = CreateStorage(root);
            var commitScope = new PersistenceCommitScope();
            var persistentStorage = new LocalPersistenceFileStorageService(
                storage,
                storage,
                new AlwaysAcquiredPersistenceFileLeaseManager(),
                commitScope,
                NullLogger<LocalPersistenceFileStorageService>.Instance);

            Func<Task> invalidStage = () => persistentStorage.StageAsync(
                new MemoryStream([0]),
                string.Empty);
            await invalidStage.Should().ThrowAsync<ArgumentException>();
            commitScope.CurrentCommitId.Should().BeNull();

            var committedStage = await persistentStorage.StageAsync(
                new MemoryStream([1, 2, 3]),
                "committed.txt");
            (await storage.ExistsAsync(committedStage.CommitId)).Should().BeTrue();
            (await FileExistsAsync(storage, committedStage.StoragePath)).Should().BeTrue();
            commitScope.CurrentCommitId.Should().Be(committedStage.CommitId);

            commitScope.ReleaseCommitId(committedStage.CommitId);
            Func<Task> overlappingStage = () => persistentStorage.StageAsync(
                new MemoryStream([9]),
                "overlapping.txt");
            await overlappingStage.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*pending persistence file stage*");

            var protectedPath = await CreateStoredFileAsync(
                root,
                "protected.txt",
                [8, 8, 8]);
            Func<Task> forgedRollback = () => persistentStorage.RollbackBestEffortAsync(
                new PersistenceFileStage(committedStage.CommitId, protectedPath));
            await forgedRollback.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*does not match the active reconciliation lease*");
            (await FileExistsAsync(storage, protectedPath)).Should().BeTrue();

            await persistentStorage.ConfirmBestEffortAsync(committedStage);
            (await storage.ExistsAsync(committedStage.CommitId)).Should().BeFalse();
            (await FileExistsAsync(storage, committedStage.StoragePath)).Should().BeTrue();
            commitScope.CurrentCommitId.Should().BeNull();

            var rolledBackStage = await persistentStorage.StageAsync(
                new MemoryStream([4, 5, 6]),
                "rolled-back.txt");
            await persistentStorage.RollbackBestEffortAsync(rolledBackStage);
            (await storage.ExistsAsync(rolledBackStage.CommitId)).Should().BeFalse();
            (await FileExistsAsync(storage, rolledBackStage.StoragePath)).Should().BeFalse();
            commitScope.CurrentCommitId.Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PersistenceFileCommitProtocol_ShouldRollback_WhenDatabaseSaveIsOmitted()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var storage = CreateStorage(root);
            var commitScope = new PersistenceCommitScope();
            var persistentStorage = new LocalPersistenceFileStorageService(
                storage,
                storage,
                new AlwaysAcquiredPersistenceFileLeaseManager(),
                commitScope,
                NullLogger<LocalPersistenceFileStorageService>.Instance);
            var stage = await persistentStorage.StageAsync(
                new MemoryStream([1, 2, 3]),
                "missing-save.txt");

            Func<Task> action = async () => await persistentStorage.ExecuteAsync(
                stage,
                _ => Task.FromResult(42));

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*before database persistence consumes its commit id*");
            commitScope.CurrentCommitId.Should().BeNull();
            (await storage.ExistsAsync(stage.CommitId)).Should().BeFalse();
            (await FileExistsAsync(storage, stage.StoragePath)).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
