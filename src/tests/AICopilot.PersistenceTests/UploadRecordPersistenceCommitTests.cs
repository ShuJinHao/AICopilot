using AICopilot.AiGatewayService.Uploads;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.EntityFrameworkCore.Repository;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Infrastructure.Storage;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using static AICopilot.PersistenceTestKit.PersistenceFileTestStorage;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class UploadRecordPersistenceCommitTests(PostgresPersistenceFixture fixture)
{
    [Theory]
    [InlineData(UploadRecordScope.SessionTemp)]
    [InlineData(UploadRecordScope.AgentInput)]
    public async Task ActiveUpload_ShouldCommitRecordAuditMarkerAndFileTogether(
        UploadRecordScope scope)
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var storageRoot = CreateTemporaryRoot();
        try
        {
            await using var persistence = UploadPersistenceScope.Create(database.ConnectionString);
            var storage = CreateStorage(storageRoot);
            var persistentStorage = CreatePersistentStorage(
                database.ConnectionString,
                persistence.CommitScope,
                storage);

            var result = await persistence.CreateCoordinator(persistentStorage).UploadAsync(
                CreateCommand(persistence, scope),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            await using var verification = CreateAiGatewayContext(database.ConnectionString);
            var upload = await verification.UploadRecords.SingleAsync();
            upload.Id.Value.Should().Be(result.Value!.Id);
            upload.StoragePath.Should().NotBeNullOrWhiteSpace();
            if (scope == UploadRecordScope.SessionTemp)
            {
                upload.SessionId.Should().Be(persistence.Session.Id);
                upload.AgentTaskId.Should().BeNull();
            }
            else
            {
                upload.AgentTaskId.Should().Be(persistence.Task.Id);
                upload.SessionId.Should().BeNull();
            }
            await using var audit = new AuditDbContext(
                PostgresPersistenceTestOptions.CreateAudit(database.ConnectionString));
            (await audit.AuditLogs.CountAsync(entry => entry.ActionCode == "AiGateway.Upload"))
                .Should().Be(1);
            await using var markers = new PersistenceCommitMarkerDbContext(
                PostgresPersistenceTestOptions.CreateMarker(database.ConnectionString));
            var marker = await markers.CommitMarkers.SingleAsync();
            (await storage.ExistsAsync(marker.Id)).Should().BeFalse();
            (await FileExistsAsync(storage, upload.StoragePath!)).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [Fact]
    public async Task SessionUpload_ShouldRollbackFileAndJournal_WhenBusinessSaveFailsKnown()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var storageRoot = CreateTemporaryRoot();
        try
        {
            await using var persistence = UploadPersistenceScope.Create(
                database.ConnectionString,
                [new KnownBusinessSaveFailureInterceptor()]);
            var storage = CreateStorage(storageRoot);
            var persistentStorage = CreatePersistentStorage(
                database.ConnectionString,
                persistence.CommitScope,
                storage);

            Func<Task> action = async () => await persistence
                .CreateCoordinator(persistentStorage)
                .UploadAsync(
                    CreateCommand(persistence, UploadRecordScope.SessionTemp),
                    CancellationToken.None);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Simulated known business save failure.");
            await AssertDatabaseCountsAsync(database.ConnectionString, 0, 0, 0);
            var pending = await storage.GetPendingAsync(
                10,
                DateTime.UtcNow.AddMinutes(1));
            pending.Records.Should().BeEmpty();
            Directory.EnumerateFiles(storageRoot, "*", SearchOption.AllDirectories)
                .Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [Fact]
    public async Task SessionUpload_ShouldLeavePendingAndReconcile_WhenCommitOutcomeIsUnknown()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var storageRoot = CreateTemporaryRoot();
        try
        {
            var commitFault = new CommitAcknowledgementLostInterceptor();
            var markerFault = new FailMarkerQueryInterceptor(int.MaxValue);
            await using var persistence = UploadPersistenceScope.Create(
                database.ConnectionString,
                [commitFault],
                markerFault);
            var storage = CreateStorage(storageRoot);
            var persistentStorage = CreatePersistentStorage(
                database.ConnectionString,
                persistence.CommitScope,
                storage);

            Func<Task> action = async () => await persistence
                .CreateCoordinator(persistentStorage)
                .UploadAsync(
                    CreateCommand(persistence, UploadRecordScope.SessionTemp),
                    CancellationToken.None);

            var thrown = await action.Should().ThrowAsync<PersistenceCommitOutcomeUnknownException>();
            commitFault.ThrowCount.Should().Be(1);
            await AssertDatabaseCountsAsync(database.ConnectionString, 1, 1, 1);
            var pending = await storage.GetPendingAsync(
                10,
                DateTime.UtcNow.AddMinutes(1));
            var record = pending.Records.Should().ContainSingle().Subject;
            record.CommitId.Should().Be(thrown.Which.CommitId);
            (await FileExistsAsync(storage, record.StoragePath)).Should().BeTrue();

            await using var markers = new PersistenceCommitMarkerDbContext(
                PostgresPersistenceTestOptions.CreateMarker(database.ConnectionString));
            var maintenance = new PersistenceFileMaintenanceService(
                markers,
                storage,
                new PostgresPersistenceFileReconciliationLeaseManager(
                    PostgresPersistenceTestOptions.CreateMarker(database.ConnectionString)),
                storage,
                NullLogger<PersistenceFileMaintenanceService>.Instance);
            var maintenanceResult = await maintenance.RunOnceAsync(
                DateTime.UtcNow.AddMinutes(20),
                TimeSpan.FromMinutes(10),
                TimeSpan.FromDays(30),
                10);

            maintenanceResult.ReconciledCommittedFiles.Should().Be(1);
            (await storage.ExistsAsync(record.CommitId)).Should().BeFalse();
            (await FileExistsAsync(storage, record.StoragePath)).Should().BeTrue();
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
            "aicopilot_upload_commit");
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

    private static LocalPersistenceFileStorageService CreatePersistentStorage(
        string connectionString,
        PersistenceCommitScope commitScope,
        LocalFileStorageService storage)
    {
        return new LocalPersistenceFileStorageService(
            storage,
            storage,
            new PostgresPersistenceFileReconciliationLeaseManager(
                PostgresPersistenceTestOptions.CreateMarker(connectionString)),
            commitScope,
            NullLogger<LocalPersistenceFileStorageService>.Instance);
    }

    private static UploadRecordCommand CreateCommand(
        UploadPersistenceScope persistence,
        UploadRecordScope scope)
    {
        var bytes = "session upload"u8.ToArray();
        return new UploadRecordCommand(
            scope.ToString(),
            new AiGatewayUploadStream(
                @"C:\fakepath\session-report.txt",
                "text/plain",
                bytes.Length,
                new MemoryStream(bytes)),
            SessionId: scope == UploadRecordScope.SessionTemp
                ? persistence.Session.Id.Value
                : null,
            AgentTaskId: scope == UploadRecordScope.AgentInput
                ? persistence.Task.Id.Value
                : null);
    }

    private static AiGatewayDbContext CreateAiGatewayContext(
        string connectionString,
        params IInterceptor[] interceptors)
    {
        var history = MigrationHistoryTables.AiGateway;
        var builder = new DbContextOptionsBuilder<AiGatewayDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql =>
                {
                    npgsql.MigrationsHistoryTable(history.TableName, history.Schema);
                    npgsql.EnableRetryOnFailure(2, TimeSpan.Zero, null);
                });
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return new AiGatewayDbContext(builder.Options);
    }

    private static async Task AssertDatabaseCountsAsync(
        string connectionString,
        int uploads,
        int audits,
        int markers)
    {
        await using var aiGateway = CreateAiGatewayContext(connectionString);
        (await aiGateway.UploadRecords.CountAsync()).Should().Be(uploads);
        await using var audit = new AuditDbContext(
            PostgresPersistenceTestOptions.CreateAudit(connectionString));
        (await audit.AuditLogs.CountAsync()).Should().Be(audits);
        await using var marker = new PersistenceCommitMarkerDbContext(
            PostgresPersistenceTestOptions.CreateMarker(connectionString));
        (await marker.CommitMarkers.CountAsync()).Should().Be(markers);
    }

    private sealed class UploadPersistenceScope : IAsyncDisposable
    {
        private readonly AiGatewayDbContext dbContext;
        private readonly AuditDbContext auditDbContext;

        private UploadPersistenceScope(
            AiGatewayDbContext dbContext,
            AuditDbContext auditDbContext,
            PersistenceCommitEngine engine,
            PersistenceCommitScope commitScope,
            AiGatewayRepository<UploadRecord> repository,
            Session session,
            AgentTask task,
            Guid userId)
        {
            this.dbContext = dbContext;
            this.auditDbContext = auditDbContext;
            Engine = engine;
            CommitScope = commitScope;
            Repository = repository;
            Session = session;
            Task = task;
            UserId = userId;
        }

        private PersistenceCommitEngine Engine { get; }

        public PersistenceCommitScope CommitScope { get; }

        private AiGatewayRepository<UploadRecord> Repository { get; }

        public Session Session { get; }

        public AgentTask Task { get; }

        private Guid UserId { get; }

        public static UploadPersistenceScope Create(
            string connectionString,
            IReadOnlyCollection<IInterceptor>? businessInterceptors = null,
            params IInterceptor[] markerInterceptors)
        {
            var dbContext = CreateAiGatewayContext(
                connectionString,
                businessInterceptors?.ToArray() ?? []);
            var auditDbContext = new AuditDbContext(
                PostgresPersistenceTestOptions.CreateAudit(connectionString));
            var engine = new PersistenceCommitEngine(
                PostgresPersistenceTestOptions.CreateMarker(
                    connectionString,
                    markerInterceptors));
            var commitScope = new PersistenceCommitScope();
            var committer = new RepositoryPersistenceCommitter(
                auditDbContext,
                engine,
                [new AiGatewayDomainEventOutboxSource()],
                commitScope);
            var userId = Guid.NewGuid();
            var session = new Session(userId, ConversationTemplateId.New());
            var task = new AgentTask(
                session.Id,
                userId,
                "Upload input",
                "Validate an uploaded agent input.",
                AgentTaskType.ReportGeneration,
                AgentTaskRiskLevel.Low,
                null,
                "{}",
                DateTimeOffset.UtcNow);
            return new UploadPersistenceScope(
                dbContext,
                auditDbContext,
                engine,
                commitScope,
                new AiGatewayRepository<UploadRecord>(dbContext, committer),
                session,
                task,
                userId);
        }

        public UploadRecordCoordinator CreateCoordinator(
            IPersistenceFileStorageService persistentStorage)
        {
            return new UploadRecordCoordinator(
                Repository,
                new InMemoryReadRepository<Session>([Session]),
                new InMemoryReadRepository<AgentTask>([Task]),
                persistentStorage,
                new AuditLogWriter(
                    auditDbContext,
                    Engine,
                    new TestCurrentUser(UserId)),
                new TestCurrentUser(UserId));
        }

        public async ValueTask DisposeAsync()
        {
            await auditDbContext.DisposeAsync();
            await dbContext.DisposeAsync();
        }
    }
}
