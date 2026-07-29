using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class FinalOutputFileSetRecoveryTests(PostgresPersistenceFixture fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CommitOutcomeUnknown_ShouldReconcileFromDurableDatabaseAuthorityAfterRestart(
        bool databaseCommitted)
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var storageRoot = CreateTemporaryRoot();
        try
        {
            var taskId = AgentTaskId.New();
            var workspaceId = ArtifactWorkspaceId.New();
            var nodeRunId = AgentNodeRunId.New();
            var workspaceCode = $"ws_{Guid.NewGuid():N}";
            var fileSetStore = CreateFileSetStore(database.ConnectionString, storageRoot);
            var stage = await fileSetStore.StageAsync(
                workspaceCode,
                "FinalizeArtifacts",
                "final",
                [
                    new ArtifactFileSetWriteRequest(
                        "report.md",
                        "# final output"u8.ToArray(),
                        "text/markdown")
                ],
                authority: new ArtifactFileSetAuthority(
                    taskId.Value,
                    nodeRunId.Value,
                    TaskFencingToken: 17,
                    NodeFencingToken: 29));
            var persistenceInvocations = 0;

            Func<Task> action = async () => await fileSetStore.ExecuteAsync<bool>(
                stage,
                async cancellationToken =>
                {
                    persistenceInvocations++;
                    if (databaseCommitted)
                    {
                        await CommitOutcomeAuthorityAsync(
                            database.ConnectionString,
                            stage,
                            taskId,
                            workspaceId,
                            nodeRunId,
                            cancellationToken);
                    }

                    throw new PersistenceCommitOutcomeUnknownException(
                        stage.CommitId,
                        new IOException("Simulated lost persistence acknowledgement."));
                });

            var thrown = await action.Should()
                .ThrowAsync<PersistenceCommitOutcomeUnknownException>();
            thrown.Which.CommitId.Should().Be(stage.CommitId);
            persistenceInvocations.Should().Be(1);
            (await fileSetStore.ExistsPendingAsync(stage.CommitId)).Should().BeTrue();
            (await fileSetStore.VerifyPublishedAsync(stage)).Should().BeTrue();

            var restartedStore = CreateFileSetStore(database.ConnectionString, storageRoot);
            await using var provider = CreateMaintenanceProvider(
                database.ConnectionString,
                restartedStore);
            await using (var scope = provider.CreateAsyncScope())
            {
                var firstRecovery = await scope.ServiceProvider
                    .GetRequiredService<IArtifactFileSetMaintenanceService>()
                    .RunOnceAsync(
                        DateTimeOffset.UtcNow.AddMinutes(5),
                        TimeSpan.FromSeconds(1),
                        10);

                firstRecovery.ConfirmedOperations.Should().Be(databaseCommitted ? 1 : 0);
                firstRecovery.RolledBackOperations.Should().Be(databaseCommitted ? 0 : 1);
                firstRecovery.FailedOperations.Should().Be(0);
                firstRecovery.ActiveOperations.Should().Be(0);
                firstRecovery.HasUnreadableJournal.Should().BeFalse();
            }

            persistenceInvocations.Should().Be(1, "recovery must not replay the finalization callback");
            (await restartedStore.ExistsPendingAsync(stage.CommitId)).Should().BeFalse();
            (await restartedStore.VerifyPublishedAsync(stage)).Should().Be(databaseCommitted);

            await using (var scope = provider.CreateAsyncScope())
            {
                var repeatedRecovery = await scope.ServiceProvider
                    .GetRequiredService<IArtifactFileSetMaintenanceService>()
                    .RunOnceAsync(
                        DateTimeOffset.UtcNow.AddMinutes(5),
                        TimeSpan.FromSeconds(1),
                        10);

                repeatedRecovery.ConfirmedOperations.Should().Be(0);
                repeatedRecovery.RolledBackOperations.Should().Be(0);
                repeatedRecovery.FailedOperations.Should().Be(0);
                repeatedRecovery.ActiveOperations.Should().Be(0);
                repeatedRecovery.HasUnreadableJournal.Should().BeFalse();
            }

            await using var aiGateway = CreateAiGatewayContext(database.ConnectionString);
            await using var markers = new PersistenceCommitMarkerDbContext(
                PostgresPersistenceTestOptions.CreateMarker(database.ConnectionString));
            (await aiGateway.ArtifactFileSetOperations.CountAsync())
                .Should().Be(databaseCommitted ? 1 : 0);
            (await markers.CommitMarkers.CountAsync())
                .Should().Be(databaseCommitted ? 1 : 0);
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptedJournal_ShouldRemainFailClosedAcrossRepeatedRestartRecovery()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var storageRoot = CreateTemporaryRoot();
        try
        {
            var workspaceCode = $"ws_{Guid.NewGuid():N}";
            var taskId = AgentTaskId.New();
            var nodeRunId = AgentNodeRunId.New();
            var fileSetStore = CreateFileSetStore(database.ConnectionString, storageRoot);
            var stage = await fileSetStore.StageAsync(
                workspaceCode,
                "FinalizeArtifacts",
                "final",
                [
                    new ArtifactFileSetWriteRequest(
                        "report.md",
                        "# final output"u8.ToArray(),
                        "text/markdown")
                ],
                authority: new ArtifactFileSetAuthority(
                    taskId.Value,
                    nodeRunId.Value,
                    TaskFencingToken: 31,
                    NodeFencingToken: 37));
            await fileSetStore.LeavePendingAsync(stage);
            var journalPath = Path.Combine(
                storageRoot,
                ".persistence",
                "artifact-file-sets",
                "journal",
                $"{stage.CommitId:N}.json");
            await File.WriteAllTextAsync(journalPath, "{not-json");

            var restartedStore = CreateFileSetStore(database.ConnectionString, storageRoot);
            await using var provider = CreateMaintenanceProvider(
                database.ConnectionString,
                restartedStore);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await using var scope = provider.CreateAsyncScope();
                var recovery = await scope.ServiceProvider
                    .GetRequiredService<IArtifactFileSetMaintenanceService>()
                    .RunOnceAsync(
                        DateTimeOffset.UtcNow.AddMinutes(5),
                        TimeSpan.FromSeconds(1),
                        10);

                recovery.ConfirmedOperations.Should().Be(0);
                recovery.RolledBackOperations.Should().Be(0);
                recovery.FailedOperations.Should().Be(0);
                recovery.ActiveOperations.Should().Be(0);
                recovery.HasUnreadableJournal.Should().BeTrue();
                File.Exists(journalPath).Should().BeTrue();
                (await restartedStore.VerifyPublishedAsync(stage)).Should().BeTrue(
                    "an unreadable journal cannot authorize confirmation or rollback");
            }
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    private async Task<PostgresScratchDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_final_output_recovery");
        try
        {
            await using var root = new AiCopilotDbContext(
                PostgresPersistenceTestOptions.Create<AiCopilotDbContext>(
                    database.ConnectionString,
                    MigrationHistoryTables.AiCopilot));
            await root.Database.MigrateAsync();
            await using var aiGateway = CreateAiGatewayContext(database.ConnectionString);
            await aiGateway.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private static async Task CommitOutcomeAuthorityAsync(
        string connectionString,
        ArtifactFileSetStage stage,
        AgentTaskId taskId,
        ArtifactWorkspaceId workspaceId,
        AgentNodeRunId nodeRunId,
        CancellationToken cancellationToken)
    {
        await using var aiGateway = CreateAiGatewayContext(connectionString);
        var operation = ArtifactFileSetOperationFactory.CreateCompleted(
            stage,
            taskId,
            workspaceId,
            nodeRunId,
            stage.Authority.TaskFencingToken,
            stage.Authority.NodeFencingToken,
            DateTimeOffset.UtcNow);
        var participant = new FileSetCommitParticipant(aiGateway, operation);
        var engine = new PersistenceCommitEngine(
            PostgresPersistenceTestOptions.CreateMarker(connectionString));
        await engine.CommitAsync(
            "Agent.FinalOutput",
            participant,
            cancellationToken,
            stage.CommitId);
    }

    private static LocalArtifactWorkspaceFileSetStore CreateFileSetStore(
        string connectionString,
        string storageRoot)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ArtifactWorkspace:RootPath"] = storageRoot
            })
            .Build();
        var fileStore = new LocalArtifactWorkspaceFileStore(configuration);
        return new LocalArtifactWorkspaceFileSetStore(
            fileStore,
            new PersistenceCommitScope(),
            new PostgresPersistenceFileReconciliationLeaseManager(
                PostgresPersistenceTestOptions.CreateMarker(connectionString)),
            NullLogger<LocalArtifactWorkspaceFileSetStore>.Instance);
    }

    private static ServiceProvider CreateMaintenanceProvider(
        string connectionString,
        IArtifactWorkspaceFileSetStore fileSetStore)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => CreateAiGatewayContext(connectionString));
        services.AddScoped(_ => new PersistenceCommitMarkerDbContext(
            PostgresPersistenceTestOptions.CreateMarker(connectionString)));
        services.AddSingleton(fileSetStore);
        services.AddSingleton<IPersistenceFileReconciliationLeaseManager>(
            new PostgresPersistenceFileReconciliationLeaseManager(
                PostgresPersistenceTestOptions.CreateMarker(connectionString)));
        services.AddArtifactFileSetMaintenance();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static AiGatewayDbContext CreateAiGatewayContext(string connectionString) =>
        new(PostgresPersistenceTestOptions.Create<AiGatewayDbContext>(
            connectionString,
            MigrationHistoryTables.AiGateway));

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-final-output-recovery",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FileSetCommitParticipant(
        AiGatewayDbContext dbContext,
        ArtifactFileSetOperation operation)
        : IPersistenceCommitParticipant<bool>
    {
        public DbContext TransactionOwner => dbContext;

        public async Task<PersistenceAttemptResult<bool>> PersistAttemptAsync(
            PersistenceAttemptContext context,
            CancellationToken cancellationToken)
        {
            _ = context;
            dbContext.ArtifactFileSetOperations.Add(operation);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new PersistenceAttemptResult<bool>(true, HasPersistentChanges: true);
        }

        public void CommitConfirmed(bool result)
        {
            result.Should().BeTrue();
        }
    }
}
