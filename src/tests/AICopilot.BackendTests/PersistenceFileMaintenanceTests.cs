using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Infrastructure.Storage;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static AICopilot.BackendTests.PersistenceFileTestStorage;

namespace AICopilot.BackendTests;

[Trait("Suite", "PersistenceFileStorage")]
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

[Collection(PostgresPersistenceTestCollection.Name)]
[Trait("Suite", "PersistenceCommit")]
[Trait("Runtime", "DockerRequired")]
public sealed class PersistenceFileMaintenanceTests(PostgresPersistenceFixture fixture)
{
    [Fact]
    public async Task Maintenance_ShouldKeepCommittedFileAndDeleteRolledBackFile()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var root = CreateTemporaryRoot();
        try
        {
            var utcNow = DateTime.UtcNow;
            var storage = CreateStorage(root);
            var committedPath = await CreateStoredFileAsync(
                root,
                "committed.txt",
                [1, 2, 3]);
            var rolledBackPath = await CreateStoredFileAsync(
                root,
                "rolled-back.txt",
                [4, 5, 6]);
            var committedId = Guid.NewGuid();
            var rolledBackId = Guid.NewGuid();
            await storage.StageAsync(
                new PersistenceFileReconciliationRecord(
                    committedId,
                    committedPath,
                    utcNow.AddMinutes(-20)));
            await storage.StageAsync(
                new PersistenceFileReconciliationRecord(
                    rolledBackId,
                    rolledBackPath,
                    utcNow.AddMinutes(-20)));
            await AddMarkersAsync(
                database.ConnectionString,
                new PersistenceCommitMarker(
                    committedId,
                    "Repository:RagDbContext",
                    utcNow.AddMinutes(-20)));

            await using var markers = CreateMarkerContext(database.ConnectionString);
            var maintenance = new PersistenceFileMaintenanceService(
                markers,
                storage,
                CreateLeaseManager(database.ConnectionString),
                storage,
                NullLogger<PersistenceFileMaintenanceService>.Instance);
            var result = await maintenance.RunOnceAsync(
                utcNow,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromDays(30),
                100);

            result.ReconciledCommittedFiles.Should().Be(1);
            result.ReconciledRolledBackFiles.Should().Be(1);
            result.FailedFileReconciliations.Should().Be(0);
            (await FileExistsAsync(storage, committedPath)).Should().BeTrue();
            (await FileExistsAsync(storage, rolledBackPath)).Should().BeFalse();
            (await storage.ExistsAsync(committedId)).Should().BeFalse();
            (await storage.ExistsAsync(rolledBackId)).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Maintenance_ShouldRetainPendingAndRecentMarkersAndDeleteExpiredUnreferencedMarker()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var root = CreateTemporaryRoot();
        try
        {
            var utcNow = DateTime.UtcNow;
            var storage = CreateStorage(root);
            var pendingId = Guid.NewGuid();
            var expiredId = Guid.NewGuid();
            var recentId = Guid.NewGuid();
            await AddMarkersAsync(
                database.ConnectionString,
                new PersistenceCommitMarker(pendingId, "pending", utcNow.AddDays(-50)),
                new PersistenceCommitMarker(expiredId, "expired", utcNow.AddDays(-40)),
                new PersistenceCommitMarker(recentId, "recent", utcNow.AddDays(-1)));
            await storage.StageAsync(
                new PersistenceFileReconciliationRecord(
                    pendingId,
                    "uploads/pending.txt",
                    utcNow.AddMinutes(-1)));

            await using var markers = CreateMarkerContext(database.ConnectionString);
            var maintenance = new PersistenceFileMaintenanceService(
                markers,
                storage,
                CreateLeaseManager(database.ConnectionString),
                storage,
                NullLogger<PersistenceFileMaintenanceService>.Instance);
            var result = await maintenance.RunOnceAsync(
                utcNow,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromDays(30),
                1);

            result.DeletedCommitMarkers.Should().Be(1);
            result.MarkerCleanupSkipped.Should().BeFalse();
            var remaining = await markers.CommitMarkers
                .AsNoTracking()
                .Select(marker => marker.Id)
                .ToArrayAsync();
            remaining.Should().BeEquivalentTo([pendingId, recentId]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Maintenance_ShouldDeferFailedJournalSoLaterEntriesAreNotStarved()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var root = CreateTemporaryRoot();
        try
        {
            var utcNow = DateTime.UtcNow;
            var storage = CreateStorage(root);
            var missingCommittedId = Guid.NewGuid();
            var rolledBackId = Guid.NewGuid();
            var unrelatedExpiredMarkerId = Guid.NewGuid();
            var rolledBackPath = await CreateStoredFileAsync(
                root,
                "later-rolled-back.txt",
                [4, 5, 6]);
            await storage.StageAsync(
                new PersistenceFileReconciliationRecord(
                    missingCommittedId,
                    "uploads/missing-first.txt",
                    utcNow.AddDays(-40)));
            await storage.StageAsync(
                new PersistenceFileReconciliationRecord(
                    rolledBackId,
                    rolledBackPath,
                    utcNow.AddMinutes(-20)));
            await AddMarkersAsync(
                database.ConnectionString,
                new PersistenceCommitMarker(
                    missingCommittedId,
                    "Repository:RagDbContext",
                    utcNow.AddDays(-40)),
                new PersistenceCommitMarker(
                    unrelatedExpiredMarkerId,
                    "expired-without-journal",
                    utcNow.AddDays(-40)));

            await using var markers = CreateMarkerContext(database.ConnectionString);
            var maintenance = new PersistenceFileMaintenanceService(
                markers,
                storage,
                CreateLeaseManager(database.ConnectionString),
                storage,
                NullLogger<PersistenceFileMaintenanceService>.Instance);

            var failedResult = await maintenance.RunOnceAsync(
                utcNow,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromDays(30),
                1);
            failedResult.FailedFileReconciliations.Should().Be(1);
            failedResult.MarkerCleanupSkipped.Should().BeFalse();
            failedResult.DeletedCommitMarkers.Should().Be(1);
            (await markers.CommitMarkers.AnyAsync(marker => marker.Id == missingCommittedId))
                .Should().BeTrue();
            (await markers.CommitMarkers.AnyAsync(marker => marker.Id == unrelatedExpiredMarkerId))
                .Should().BeFalse();

            var nextResult = await maintenance.RunOnceAsync(
                utcNow,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromDays(30),
                1);
            nextResult.ReconciledRolledBackFiles.Should().Be(1);
            (await FileExistsAsync(storage, rolledBackPath)).Should().BeFalse();
            (await storage.ExistsAsync(rolledBackId)).Should().BeFalse();
            (await storage.ExistsAsync(missingCommittedId)).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Maintenance_ShouldKeepJournalWhenCommittedFileIsMissing()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var root = CreateTemporaryRoot();
        try
        {
            var utcNow = DateTime.UtcNow;
            var storage = CreateStorage(root);
            var commitId = Guid.NewGuid();
            await storage.StageAsync(
                new PersistenceFileReconciliationRecord(
                    commitId,
                    "uploads/missing.txt",
                    utcNow.AddMinutes(-20)));
            await AddMarkersAsync(
                database.ConnectionString,
                new PersistenceCommitMarker(
                    commitId,
                    "Repository:RagDbContext",
                    utcNow.AddMinutes(-20)));

            await using var markers = CreateMarkerContext(database.ConnectionString);
            var maintenance = new PersistenceFileMaintenanceService(
                markers,
                storage,
                CreateLeaseManager(database.ConnectionString),
                storage,
                NullLogger<PersistenceFileMaintenanceService>.Instance);
            var result = await maintenance.RunOnceAsync(
                utcNow,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromDays(30),
                100);

            result.ReconciledCommittedFiles.Should().Be(0);
            result.FailedFileReconciliations.Should().Be(1);
            result.MarkerCleanupSkipped.Should().BeFalse();
            (await storage.ExistsAsync(commitId)).Should().BeTrue();
            (await markers.CommitMarkers.AnyAsync(marker => marker.Id == commitId)).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Maintenance_ShouldFailClosedWhenJournalIsUnreadable()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var root = CreateTemporaryRoot();
        try
        {
            var utcNow = DateTime.UtcNow;
            var storage = CreateStorage(root);
            var markerId = Guid.NewGuid();
            await AddMarkersAsync(
                database.ConnectionString,
                new PersistenceCommitMarker(markerId, "corrupt-journal", utcNow.AddDays(-40)));
            var journalDirectory = Path.Combine(root, ".persistence", "file-reconciliation");
            Directory.CreateDirectory(journalDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(journalDirectory, $"{markerId:N}.json"),
                "{not-json");

            await using var markers = CreateMarkerContext(database.ConnectionString);
            var maintenance = new PersistenceFileMaintenanceService(
                markers,
                storage,
                CreateLeaseManager(database.ConnectionString),
                storage,
                NullLogger<PersistenceFileMaintenanceService>.Instance);
            var result = await maintenance.RunOnceAsync(
                utcNow,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromDays(30),
                100);

            result.MarkerCleanupSkipped.Should().BeTrue();
            result.DeletedCommitMarkers.Should().Be(0);
            (await markers.CommitMarkers.AnyAsync(marker => marker.Id == markerId)).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Maintenance_ShouldFailClosedWhenJournalExistenceCannotBeVerified()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var root = CreateTemporaryRoot();
        try
        {
            var utcNow = DateTime.UtcNow;
            var storage = CreateStorage(root);
            var markerId = Guid.NewGuid();
            await AddMarkersAsync(
                database.ConnectionString,
                new PersistenceCommitMarker(markerId, "journal-io-fault", utcNow.AddDays(-40)));

            await using var markers = CreateMarkerContext(database.ConnectionString);
            var maintenance = new PersistenceFileMaintenanceService(
                markers,
                new ExistenceFaultPersistenceFileJournal(storage),
                CreateLeaseManager(database.ConnectionString),
                storage,
                NullLogger<PersistenceFileMaintenanceService>.Instance);

            Func<Task> action = () => maintenance.RunOnceAsync(
                utcNow,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromDays(30),
                100);

            await action.Should().ThrowAsync<IOException>()
                .WithMessage("simulated journal existence failure");
            (await markers.CommitMarkers.AnyAsync(marker => marker.Id == markerId)).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Maintenance_ShouldSkipFileWhileItsPostgresLeaseIsActive()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var root = CreateTemporaryRoot();
        try
        {
            var utcNow = DateTime.UtcNow;
            var storage = CreateStorage(root);
            var storagePath = await CreateStoredFileAsync(
                root,
                "active.txt",
                [7, 8, 9]);
            var commitId = Guid.NewGuid();
            await storage.StageAsync(
                new PersistenceFileReconciliationRecord(
                    commitId,
                    storagePath,
                    utcNow.AddMinutes(-20)));
            var leaseManager = CreateLeaseManager(database.ConnectionString);
            await using var activeLease = await leaseManager.TryAcquireAsync(commitId);
            activeLease.Should().NotBeNull();

            await using var markers = CreateMarkerContext(database.ConnectionString);
            var maintenance = new PersistenceFileMaintenanceService(
                markers,
                storage,
                leaseManager,
                storage,
                NullLogger<PersistenceFileMaintenanceService>.Instance);
            var result = await maintenance.RunOnceAsync(
                utcNow,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromDays(30),
                100);

            result.SkippedActiveFileReconciliations.Should().Be(1);
            (await FileExistsAsync(storage, storagePath)).Should().BeTrue();
            (await storage.ExistsAsync(commitId)).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private async Task<PostgresScratchDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_file_maintenance");
        try
        {
            await using var root = new AiCopilotDbContext(
                PostgresPersistenceTestOptions.Create<AiCopilotDbContext>(
                    database.ConnectionString,
                    MigrationHistoryTables.AiCopilot));
            await root.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private static async Task AddMarkersAsync(
        string connectionString,
        params PersistenceCommitMarker[] markers)
    {
        await using var dbContext = CreateMarkerContext(connectionString);
        dbContext.CommitMarkers.AddRange(markers);
        await dbContext.SaveChangesAsync();
    }

    private static PersistenceCommitMarkerDbContext CreateMarkerContext(string connectionString)
    {
        return new PersistenceCommitMarkerDbContext(
            PostgresPersistenceTestOptions.CreateMarker(connectionString));
    }

    private static PostgresPersistenceFileReconciliationLeaseManager CreateLeaseManager(
        string connectionString)
    {
        return new PostgresPersistenceFileReconciliationLeaseManager(
            PostgresPersistenceTestOptions.CreateMarker(connectionString));
    }

}
