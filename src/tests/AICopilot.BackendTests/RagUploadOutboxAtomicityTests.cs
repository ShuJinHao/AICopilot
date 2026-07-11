using System.Text.Json;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.EntityFrameworkCore.Repository;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Infrastructure.Storage;
using AICopilot.RagService.Commands.Documents;
using AICopilot.RagService.Documents;
using AICopilot.RagWorker.Consumers;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using static AICopilot.BackendTests.PersistenceFileTestStorage;

namespace AICopilot.BackendTests;

[Collection(PostgresPersistenceTestCollection.Name)]
[Trait("Suite", "PersistenceCommit")]
[Trait("Runtime", "DockerRequired")]
public sealed class RagUploadOutboxAtomicityTests(PostgresPersistenceFixture fixture)
{
    [Fact]
    public async Task RepositoryCommit_ShouldRejectStagedFileWithoutBusinessRowChange()
    {
        await using var database = await CreateDatabaseAsync();
        await MigrateRagStoreAsync(database.ConnectionString);
        await using var persistence = RagPersistenceScope.Create(database.ConnectionString);
        var fileStorage = new CapturingFileStorage();
        fileStorage.AttachCommitScope(persistence.CommitScope);
        var fileStage = await fileStorage.StageAsync(
            new MemoryStream([1, 2, 3]),
            "orphan.txt");
        await persistence.AuditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Rag,
                "Rag.OrphanFileGuard",
                "KnowledgeDocument",
                null,
                "orphan.txt",
                AuditResults.Rejected,
                "Verify that an audit row cannot confirm a staged file without a business row."));

        Func<Task> action = () => persistence.Repository.SaveChangesAsync();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*committed business row change*");
        await fileStorage.RollbackBestEffortAsync(fileStage);
        await AssertCommitCountsAsync(database.ConnectionString, 0, 0, 0, 0);
    }

    [Fact]
    public async Task UploadDocument_ShouldCommitDocumentAuditOutboxAndMarkerTogether()
    {
        await using var database = await CreateDatabaseAsync();
        await MigrateRagStoreAsync(database.ConnectionString);
        var knowledgeBaseId = await SeedKnowledgeBaseAsync(database.ConnectionString);
        await using var persistence = RagPersistenceScope.Create(database.ConnectionString);
        var fileStorage = new CapturingFileStorage();

        var result = await persistence.CreateUploadHandler(fileStorage).Handle(
            new UploadDocumentCommand(
                knowledgeBaseId,
                new FileUploadStream("atomic.txt", new MemoryStream([1, 2, 3]))),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fileStorage.SaveCount.Should().Be(1);
        fileStorage.DeleteCount.Should().Be(0);
        fileStorage.ConfirmCount.Should().Be(1);

        await using var verifyRag = new RagDbContext(CreateRagOptions(database.ConnectionString));
        var document = await verifyRag.Documents.SingleAsync();
        document.Id.Value.Should().Be(result.Value!.Id);
        await using var verifyOutbox = CreateOutboxContext(database.ConnectionString);
        var outboxMessage = await verifyOutbox.OutboxMessages.SingleAsync();
        outboxMessage.EventTypeName.Should().Be(typeof(DocumentUploadedEvent).FullName);

        using var payload = JsonDocument.Parse(outboxMessage.Payload);
        payload.RootElement.GetProperty(nameof(DocumentUploadedEvent.DocumentId)).GetInt32()
            .Should().Be(document.Id.Value);
        payload.RootElement.GetProperty(nameof(DocumentUploadedEvent.KnowledgeBaseId)).GetGuid()
            .Should().Be(knowledgeBaseId);
        payload.RootElement.GetProperty(nameof(DocumentUploadedEvent.FilePath)).GetString()
            .Should().Be(document.FilePath);
        payload.RootElement.GetProperty(nameof(DocumentUploadedEvent.FileName)).GetString()
            .Should().Be("atomic.txt");

        await AssertCommitCountsAsync(database.ConnectionString, 1, 1, 1, 1);
    }

    [Fact]
    public async Task UploadDocument_ShouldRetryPreCommitTransientFailure_WithStableDocumentId()
    {
        await using var database = await CreateDatabaseAsync();
        await MigrateRagStoreAsync(database.ConnectionString);
        var knowledgeBaseId = await SeedKnowledgeBaseAsync(database.ConnectionString);
        var saveFault = new FailFirstBusinessSaveInterceptor();
        var ragOptions = CreateRagOptions(database.ConnectionString, saveFault);
        await using var persistence = RagPersistenceScope.Create(database.ConnectionString, ragOptions);
        var fileStorage = new CapturingFileStorage();

        var result = await persistence.CreateUploadHandler(fileStorage).Handle(
            new UploadDocumentCommand(
                knowledgeBaseId,
                new FileUploadStream("retry.txt", new MemoryStream([1, 2, 3]))),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        saveFault.SaveAttemptCount.Should().Be(2);
        fileStorage.SaveCount.Should().Be(1);
        fileStorage.DeleteCount.Should().Be(0);
        fileStorage.ConfirmCount.Should().Be(1);
        await using var verifyRag = new RagDbContext(CreateRagOptions(database.ConnectionString));
        (await verifyRag.Documents.SingleAsync()).Id.Value.Should().Be(result.Value!.Id);
        await AssertCommitCountsAsync(database.ConnectionString, 1, 1, 1, 1);
    }

    [Fact]
    public async Task RepositoryCommit_ShouldReplayDatabaseGeneratedChunkIdentity_WhenFailureOccursAfterBusinessSave()
    {
        await using var database = await CreateDatabaseAsync();
        await MigrateRagStoreAsync(database.ConnectionString);
        var seed = await SeedSplittingDocumentAsync(database.ConnectionString);
        var saveCounter = new BusinessSaveCounterInterceptor();
        var ragOptions = CreateRagOptions(database.ConnectionString, saveCounter);
        var failAfterBusinessSave = new FailFirstOutboxMaterializationSourceFactory();
        await using var persistence = RagPersistenceScope.CreateWithOutboxSource(
            database.ConnectionString,
            ragOptions,
            failAfterBusinessSave.Create);
        var knowledgeBase = await persistence.Repository.GetAsync(
            item => item.Id == seed.KnowledgeBaseId,
            includes: [item => item.Documents],
            CancellationToken.None);
        knowledgeBase.Should().NotBeNull();
        var document = knowledgeBase!.Documents.Should().ContainSingle().Subject;
        document.Id.Value.Should().Be(seed.DocumentId);
        document.AddChunk(0, "generated identity replay chunk");
        persistence.EventBuffer.Stage(() => new DocumentUploadedEvent
        {
            DocumentId = document.Id,
            KnowledgeBaseId = knowledgeBase.Id,
            FilePath = document.FilePath,
            FileName = document.Name
        });
        await persistence.AuditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Rag,
                "Rag.GeneratedIdentityReplay",
                "KnowledgeDocument",
                document.Id.ToString(),
                document.Name,
                AuditResults.Succeeded,
                "Exercise generated child identity replay after business SaveChanges(false)."));

        await persistence.Repository.SaveChangesAsync();

        saveCounter.SaveAttemptCount.Should().Be(2);
        failAfterBusinessSave.MaterializeAttemptCount.Should().Be(2);
        await using var verifyRag = new RagDbContext(CreateRagOptions(database.ConnectionString));
        var chunk = await verifyRag.DocumentChunks.SingleAsync();
        chunk.Id.Should().BeGreaterThan(0);
        chunk.DocumentId.Value.Should().Be(seed.DocumentId);
        chunk.Content.Should().Be("generated identity replay chunk");
        await AssertCommitCountsAsync(database.ConnectionString, 1, 1, 1, 1);
    }

    [Fact]
    public async Task UploadDocument_ShouldNotReplayCommittedWrite_WhenCommitAcknowledgementIsLost()
    {
        await using var database = await CreateDatabaseAsync();
        await MigrateRagStoreAsync(database.ConnectionString);
        var knowledgeBaseId = await SeedKnowledgeBaseAsync(database.ConnectionString);
        var commitFault = new CommitAcknowledgementLostInterceptor();
        var saveCounter = new BusinessSaveCounterInterceptor();
        var ragOptions = CreateRagOptions(database.ConnectionString, saveCounter, commitFault);
        await using var persistence = RagPersistenceScope.Create(database.ConnectionString, ragOptions);
        var fileStorage = new CapturingFileStorage();

        var result = await persistence.CreateUploadHandler(fileStorage).Handle(
            new UploadDocumentCommand(
                knowledgeBaseId,
                new FileUploadStream("ack-lost.txt", new MemoryStream([1, 2, 3]))),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        commitFault.ThrowCount.Should().Be(1);
        saveCounter.SaveAttemptCount.Should().Be(1);
        fileStorage.SaveCount.Should().Be(1);
        fileStorage.DeleteCount.Should().Be(0);
        fileStorage.ConfirmCount.Should().Be(1);
        await AssertCommitCountsAsync(database.ConnectionString, 1, 1, 1, 1);
    }

    [Fact]
    public async Task UploadDocument_ShouldRetryFreshMarkerVerification_WithoutReplayingBusiness()
    {
        await using var database = await CreateDatabaseAsync();
        await MigrateRagStoreAsync(database.ConnectionString);
        var knowledgeBaseId = await SeedKnowledgeBaseAsync(database.ConnectionString);
        var commitFault = new CommitAcknowledgementLostInterceptor();
        var saveCounter = new BusinessSaveCounterInterceptor();
        var markerFault = new FailMarkerQueryInterceptor(remainingFailures: 1);
        var ragOptions = CreateRagOptions(database.ConnectionString, saveCounter, commitFault);
        await using var persistence = RagPersistenceScope.Create(
            database.ConnectionString,
            ragOptions,
            markerFault);

        var result = await persistence.CreateUploadHandler(new CapturingFileStorage()).Handle(
            new UploadDocumentCommand(
                knowledgeBaseId,
                new FileUploadStream("verify-retry.txt", new MemoryStream([1, 2, 3]))),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        markerFault.QueryAttemptCount.Should().Be(2);
        saveCounter.SaveAttemptCount.Should().Be(1);
        await AssertCommitCountsAsync(database.ConnectionString, 1, 1, 1, 1);
    }

    [Fact]
    public async Task UploadDocument_ShouldKeepFile_WhenCommittedMarkerCannotBeVerified()
    {
        await using var database = await CreateDatabaseAsync();
        await MigrateRagStoreAsync(database.ConnectionString);
        var knowledgeBaseId = await SeedKnowledgeBaseAsync(database.ConnectionString);
        var commitFault = new CommitAcknowledgementLostInterceptor();
        var saveCounter = new BusinessSaveCounterInterceptor();
        var markerFault = new FailMarkerQueryInterceptor(remainingFailures: int.MaxValue);
        var ragOptions = CreateRagOptions(database.ConnectionString, saveCounter, commitFault);
        await using var persistence = RagPersistenceScope.Create(
            database.ConnectionString,
            ragOptions,
            markerFault);
        var fileStorage = new CapturingFileStorage();

        Func<Task> act = async () => await persistence.CreateUploadHandler(fileStorage).Handle(
            new UploadDocumentCommand(
                knowledgeBaseId,
                new FileUploadStream("verify-unknown.txt", new MemoryStream([1, 2, 3]))),
            CancellationToken.None);

        await act.Should().ThrowAsync<PersistenceCommitOutcomeUnknownException>();
        markerFault.QueryAttemptCount.Should().Be(3);
        saveCounter.SaveAttemptCount.Should().Be(1);
        fileStorage.SaveCount.Should().Be(1);
        fileStorage.DeleteCount.Should().Be(0);
        fileStorage.PendingCount.Should().Be(1);
        await AssertCommitCountsAsync(database.ConnectionString, 1, 1, 1, 1);
    }

    [Fact]
    public async Task UploadDocument_ShouldRetirePendingJournal_WhenDeleteEventFollowsUnknownCommit()
    {
        await using var database = await CreateDatabaseAsync();
        await MigrateRagStoreAsync(database.ConnectionString);
        var knowledgeBaseId = await SeedKnowledgeBaseAsync(database.ConnectionString);
        var commitFault = new CommitAcknowledgementLostInterceptor();
        var markerFault = new FailMarkerQueryInterceptor(remainingFailures: int.MaxValue);
        var ragOptions = CreateRagOptions(database.ConnectionString, commitFault);
        await using var persistence = RagPersistenceScope.Create(
            database.ConnectionString,
            ragOptions,
            markerFault);
        var storageRoot = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-rag-reconciliation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FileStorage:RootPath"] = storageRoot
                })
                .Build();
            var storage = new LocalFileStorageService(configuration);
            var leaseManager = new PostgresPersistenceFileReconciliationLeaseManager(
                PostgresPersistenceTestOptions.CreateMarker(database.ConnectionString));
            var persistentStorage = new LocalPersistenceFileStorageService(
                storage,
                storage,
                leaseManager,
                persistence.CommitScope,
                NullLogger<LocalPersistenceFileStorageService>.Instance);

            Func<Task> act = async () => await persistence.CreateUploadHandler(persistentStorage).Handle(
                new UploadDocumentCommand(
                    knowledgeBaseId,
                    new FileUploadStream("reconcile-later.txt", new MemoryStream([1, 2, 3]))),
                CancellationToken.None);

            var thrown = await act.Should().ThrowAsync<PersistenceCommitOutcomeUnknownException>();
            var pending = await storage.GetPendingAsync(
                10,
                DateTime.UtcNow.AddMinutes(1));
            var record = pending.Records.Should().ContainSingle().Subject;
            record.CommitId.Should().Be(thrown.Which.CommitId);
            await using (var storedFile = await storage.GetAsync(record.StoragePath))
            {
                storedFile.Should().NotBeNull();
            }

            await using var verifyRag = new RagDbContext(
                CreateRagOptions(database.ConnectionString));
            var document = await verifyRag.Documents.SingleAsync();
            await using var deletePersistence = RagPersistenceScope.Create(database.ConnectionString);
            var deleteHandler = new DeleteDocumentCommandHandler(
                deletePersistence.Repository,
                deletePersistence.EventBuffer,
                deletePersistence.AuditLogWriter,
                new TestCurrentUser(role: "Admin"));
            var deleteResult = await deleteHandler.Handle(
                new DeleteDocumentCommand(document.Id.Value),
                CancellationToken.None);
            deleteResult.IsSuccess.Should().BeTrue();

            var consumer = new DocumentFileDeletionRequestedConsumer(
                storage,
                storage,
                leaseManager,
                NullLogger<DocumentFileDeletionRequestedConsumer>.Instance);
            await consumer.DeleteFileAsync(
                new DocumentFileDeletionRequestedEvent
                {
                    DocumentId = document.Id.Value,
                    KnowledgeBaseId = document.KnowledgeBaseId.Value,
                    FilePath = document.FilePath,
                    FileName = document.Name
                });

            (await storage.ExistsAsync(record.CommitId)).Should().BeFalse();
            (await FileExistsAsync(storage, record.StoragePath)).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [Fact]
    public async Task UploadDocument_ShouldVerifyCommittedMarker_WhenCallerCancelsAndCommitAcknowledgementIsLost()
    {
        await using var database = await CreateDatabaseAsync();
        await MigrateRagStoreAsync(database.ConnectionString);
        var knowledgeBaseId = await SeedKnowledgeBaseAsync(database.ConnectionString);
        using var callerCancellation = new CancellationTokenSource();
        var cancelAtCommit = new CancelCallerAtCommitInterceptor(callerCancellation);
        var commitFault = new CommitAcknowledgementLostInterceptor();
        var markerQuery = new FailMarkerQueryInterceptor(remainingFailures: 0);
        var saveCounter = new BusinessSaveCounterInterceptor();
        var ragOptions = CreateRagOptions(
            database.ConnectionString,
            saveCounter,
            cancelAtCommit,
            commitFault);
        await using var persistence = RagPersistenceScope.Create(
            database.ConnectionString,
            ragOptions,
            markerQuery);
        var fileStorage = new CapturingFileStorage();

        var result = await persistence.CreateUploadHandler(fileStorage).Handle(
            new UploadDocumentCommand(
                knowledgeBaseId,
                new FileUploadStream("cancel-after-marker.txt", new MemoryStream([1, 2, 3]))),
            callerCancellation.Token);

        result.IsSuccess.Should().BeTrue();
        callerCancellation.IsCancellationRequested.Should().BeTrue();
        cancelAtCommit.CommitAttemptCount.Should().Be(1);
        commitFault.ThrowCount.Should().Be(1);
        markerQuery.QueryAttemptCount.Should().Be(1);
        saveCounter.SaveAttemptCount.Should().Be(1);
        fileStorage.DeleteCount.Should().Be(0);
        await AssertCommitCountsAsync(database.ConnectionString, 1, 1, 1, 1);
    }

    [Fact]
    public async Task UploadDocument_ShouldNotCommitDocumentOrOutbox_WhenStagingFails()
    {
        await using var database = await CreateDatabaseAsync();
        await MigrateRagStoreAsync(database.ConnectionString);
        var knowledgeBaseId = await SeedKnowledgeBaseAsync(database.ConnectionString);
        await using var persistence = RagPersistenceScope.Create(database.ConnectionString);
        var fileStorage = new CapturingFileStorage();
        var handler = persistence.CreateUploadHandler(
            fileStorage,
            new ThrowingEventStager(new InvalidOperationException("outbox staging failed")));

        Func<Task> act = async () => await handler.Handle(
            new UploadDocumentCommand(
                knowledgeBaseId,
                new FileUploadStream("staging-failure.txt", new MemoryStream([1, 2, 3]))),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("outbox staging failed");
        fileStorage.DeleteCount.Should().Be(1);
        fileStorage.DeletedPaths.Should().ContainSingle().Which.Should().Be("staging-failure.txt");
        await AssertCommitCountsAsync(database.ConnectionString, 0, 0, 0, 0);
    }

    [Fact]
    public async Task DeleteDocument_ShouldCommitAggregateAuditOutboxAndMarkerTogether()
    {
        await using var database = await CreateDatabaseAsync();
        await MigrateRagStoreAsync(database.ConnectionString);
        var seed = await SeedKnowledgeBaseWithDocumentAsync(database.ConnectionString);
        await using var persistence = RagPersistenceScope.Create(database.ConnectionString);
        var handler = new DeleteDocumentCommandHandler(
            persistence.Repository,
            persistence.EventBuffer,
            persistence.AuditLogWriter,
            new TestCurrentUser(role: "Admin"));

        var result = await handler.Handle(new DeleteDocumentCommand(seed.DocumentId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await using var verifyRag = new RagDbContext(CreateRagOptions(database.ConnectionString));
        var document = await verifyRag.Documents.SingleAsync();
        document.Status.Should().Be(DocumentStatus.SoftDeleted);
        await using var verifyOutbox = CreateOutboxContext(database.ConnectionString);
        var outboxMessage = await verifyOutbox.OutboxMessages.SingleAsync();
        outboxMessage.EventTypeName.Should().Be(typeof(DocumentFileDeletionRequestedEvent).FullName);

        using var payload = JsonDocument.Parse(outboxMessage.Payload);
        payload.RootElement.GetProperty(nameof(DocumentFileDeletionRequestedEvent.DocumentId)).GetInt32()
            .Should().Be(seed.DocumentId);
        payload.RootElement.GetProperty(nameof(DocumentFileDeletionRequestedEvent.KnowledgeBaseId)).GetGuid()
            .Should().Be(seed.KnowledgeBaseId);
        payload.RootElement.GetProperty(nameof(DocumentFileDeletionRequestedEvent.FilePath)).GetString()
            .Should().Be(seed.FilePath);
        payload.RootElement.GetProperty(nameof(DocumentFileDeletionRequestedEvent.FileName)).GetString()
            .Should().Be(seed.FileName);

        await using var verifyAudit = new AuditDbContext(
            PostgresPersistenceTestOptions.CreateAudit(database.ConnectionString));
        var audit = await verifyAudit.AuditLogs.SingleAsync();
        audit.ActionCode.Should().Be("Rag.DeleteDocument");
        audit.TargetId.Should().Be(seed.DocumentId.ToString());
        audit.TargetName.Should().Be(seed.FileName);
        await AssertCommitCountsAsync(database.ConnectionString, 1, 1, 1, 1);
    }

    private Task<PostgresScratchDatabase> CreateDatabaseAsync()
    {
        return PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_persistence");
    }

    private static async Task MigrateRagStoreAsync(string connectionString)
    {
        await using var aiCopilot = new AiCopilotDbContext(
            PostgresPersistenceTestOptions.Create<AiCopilotDbContext>(
                connectionString,
                MigrationHistoryTables.AiCopilot));
        await aiCopilot.Database.MigrateAsync();

        await using var rag = new RagDbContext(CreateRagOptions(connectionString));
        await rag.Database.MigrateAsync();
    }

    private static async Task<Guid> SeedKnowledgeBaseAsync(string connectionString)
    {
        await using var dbContext = new RagDbContext(CreateRagOptions(connectionString));
        var embeddingModel = new EmbeddingModel(
            "test-embedding",
            "OpenAI",
            "https://example.local",
            "text-embedding-test",
            1536,
            8191);
        var knowledgeBase = new KnowledgeBase("test-kb", "test knowledge base", embeddingModel.Id);

        dbContext.EmbeddingModels.Add(embeddingModel);
        dbContext.KnowledgeBases.Add(knowledgeBase);
        await dbContext.SaveChangesAsync();
        return knowledgeBase.Id.Value;
    }

    private static async Task<SeededDocument> SeedKnowledgeBaseWithDocumentAsync(string connectionString)
    {
        await using var dbContext = new RagDbContext(CreateRagOptions(connectionString));
        var embeddingModel = new EmbeddingModel(
            "delete-test-embedding",
            "OpenAI",
            "https://example.local",
            "text-embedding-test",
            1536,
            8191);
        var knowledgeBase = new KnowledgeBase("delete-test-kb", "test knowledge base", embeddingModel.Id);
        var document = knowledgeBase.AddDocument(
            new DocumentId(1),
            "delete-me.txt",
            "documents/delete-me.txt",
            ".txt",
            "delete-hash");

        dbContext.EmbeddingModels.Add(embeddingModel);
        dbContext.KnowledgeBases.Add(knowledgeBase);
        await dbContext.SaveChangesAsync();
        return new SeededDocument(
            knowledgeBase.Id.Value,
            document.Id.Value,
            document.FilePath,
            document.Name);
    }

    private static async Task<SeededDocument> SeedSplittingDocumentAsync(string connectionString)
    {
        await using var dbContext = new RagDbContext(CreateRagOptions(connectionString));
        var embeddingModel = new EmbeddingModel(
            "chunk-replay-embedding",
            "OpenAI",
            "https://example.local",
            "text-embedding-test",
            1536,
            8191);
        var knowledgeBase = new KnowledgeBase("chunk-replay-kb", "test knowledge base", embeddingModel.Id);
        var document = knowledgeBase.AddDocument(
            new DocumentId(1),
            "chunk-replay.txt",
            "documents/chunk-replay.txt",
            ".txt",
            "chunk-replay-hash");
        document.StartParsing();
        document.CompleteParsing();

        dbContext.EmbeddingModels.Add(embeddingModel);
        dbContext.KnowledgeBases.Add(knowledgeBase);
        await dbContext.SaveChangesAsync();
        return new SeededDocument(
            knowledgeBase.Id.Value,
            document.Id.Value,
            document.FilePath,
            document.Name);
    }

    private static DbContextOptions<RagDbContext> CreateRagOptions(
        string connectionString,
        params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<RagDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql =>
                {
                    npgsql.MigrationsHistoryTable(
                        MigrationHistoryTables.Rag.TableName,
                        MigrationHistoryTables.Rag.Schema);
                    npgsql.EnableRetryOnFailure(2, TimeSpan.Zero, null);
                });
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return builder.Options;
    }

    private static OutboxDbContext CreateOutboxContext(string connectionString)
    {
        return new OutboxDbContext(
            new DbContextOptionsBuilder<OutboxDbContext>()
                .UseNpgsql(connectionString)
                .Options);
    }

    private static async Task AssertCommitCountsAsync(
        string connectionString,
        int documents,
        int outboxMessages,
        int auditLogs,
        int commitMarkers)
    {
        await using var rag = new RagDbContext(CreateRagOptions(connectionString));
        (await rag.Documents.CountAsync()).Should().Be(documents);
        await using var outbox = CreateOutboxContext(connectionString);
        (await outbox.OutboxMessages.CountAsync()).Should().Be(outboxMessages);
        await using var audit = new AuditDbContext(
            PostgresPersistenceTestOptions.CreateAudit(connectionString));
        (await audit.AuditLogs.CountAsync()).Should().Be(auditLogs);
        await using var markers = new PersistenceCommitMarkerDbContext(
            PostgresPersistenceTestOptions.CreateMarker(connectionString));
        (await markers.CommitMarkers.CountAsync()).Should().Be(commitMarkers);
    }

    private sealed record SeededDocument(
        Guid KnowledgeBaseId,
        int DocumentId,
        string FilePath,
        string FileName);

    private sealed class RagPersistenceScope : IAsyncDisposable
    {
        private RagPersistenceScope(
            RagDbContext dbContext,
            AuditDbContext auditDbContext,
            RagRepository<KnowledgeBase> repository,
            RagIntegrationEventBuffer eventBuffer,
            AuditLogWriter auditLogWriter,
            IDocumentIdAllocator documentIdAllocator,
            PersistenceCommitScope commitScope)
        {
            DbContext = dbContext;
            AuditDbContext = auditDbContext;
            Repository = repository;
            EventBuffer = eventBuffer;
            AuditLogWriter = auditLogWriter;
            DocumentIdAllocator = documentIdAllocator;
            CommitScope = commitScope;
        }

        public RagRepository<KnowledgeBase> Repository { get; }

        public RagIntegrationEventBuffer EventBuffer { get; }

        public AuditLogWriter AuditLogWriter { get; }

        private RagDbContext DbContext { get; }

        private AuditDbContext AuditDbContext { get; }

        private IDocumentIdAllocator DocumentIdAllocator { get; }

        public PersistenceCommitScope CommitScope { get; }

        public static RagPersistenceScope Create(
            string connectionString,
            DbContextOptions<RagDbContext>? ragOptions = null,
            params IInterceptor[] markerInterceptors)
        {
            ragOptions ??= CreateRagOptions(connectionString);
            return CreateCore(
                connectionString,
                ragOptions,
                eventBuffer => eventBuffer,
                markerInterceptors);
        }

        public static RagPersistenceScope CreateWithOutboxSource(
            string connectionString,
            DbContextOptions<RagDbContext> ragOptions,
            Func<RagIntegrationEventBuffer, IPersistenceOutboxSource> outboxSourceFactory)
        {
            return CreateCore(
                connectionString,
                ragOptions,
                outboxSourceFactory,
                []);
        }

        private static RagPersistenceScope CreateCore(
            string connectionString,
            DbContextOptions<RagDbContext> ragOptions,
            Func<RagIntegrationEventBuffer, IPersistenceOutboxSource> outboxSourceFactory,
            IReadOnlyCollection<IInterceptor> markerInterceptors)
        {
            var dbContext = new RagDbContext(ragOptions);
            var auditDbContext = new AuditDbContext(
                PostgresPersistenceTestOptions.CreateAudit(connectionString));
            var eventBuffer = new RagIntegrationEventBuffer();
            var engine = new PersistenceCommitEngine(
                PostgresPersistenceTestOptions.CreateMarker(
                    connectionString,
                    markerInterceptors.ToArray()));
            var commitScope = new PersistenceCommitScope();
            var committer = new RepositoryPersistenceCommitter(
                auditDbContext,
                engine,
                [outboxSourceFactory(eventBuffer)],
                commitScope);

            return new RagPersistenceScope(
                dbContext,
                auditDbContext,
                new RagRepository<KnowledgeBase>(dbContext, committer),
                eventBuffer,
                new AuditLogWriter(auditDbContext, engine),
                new PostgresDocumentIdAllocator(ragOptions),
                commitScope);
        }

        public UploadDocumentCommandHandler CreateUploadHandler(
            IPersistenceFileStorageService fileStorage,
            IIntegrationEventStager? eventStager = null)
        {
            if (fileStorage is CapturingFileStorage capturingFileStorage)
            {
                capturingFileStorage.AttachCommitScope(CommitScope);
            }

            return new UploadDocumentCommandHandler(
                Repository,
                DocumentIdAllocator,
                fileStorage,
                new FixedDocumentFormatPolicy([".txt"]),
                eventStager ?? EventBuffer,
                AuditLogWriter,
                new TestCurrentUser(role: "Admin"));
        }

        public async ValueTask DisposeAsync()
        {
            await AuditDbContext.DisposeAsync();
            await DbContext.DisposeAsync();
        }
    }

    private sealed class ThrowingEventStager(Exception exception) : IIntegrationEventStager
    {
        public void Stage<TEvent>(Func<TEvent> messageFactory)
            where TEvent : class
        {
            throw exception;
        }
    }

    private sealed class FailFirstOutboxMaterializationSourceFactory
    {
        public int MaterializeAttemptCount { get; private set; }

        public IPersistenceOutboxSource Create(RagIntegrationEventBuffer eventBuffer)
        {
            return new Source(this, eventBuffer);
        }

        private sealed class Source(
            FailFirstOutboxMaterializationSourceFactory owner,
            RagIntegrationEventBuffer eventBuffer) : IPersistenceOutboxSource
        {
            public bool Supports(DbContext dbContext)
            {
                return eventBuffer.Supports(dbContext);
            }

            public bool HasPending(DbContext dbContext)
            {
                return eventBuffer.HasPending(dbContext);
            }

            public IReadOnlyCollection<OutboxMessage> Materialize(DbContext dbContext)
            {
                owner.MaterializeAttemptCount++;
                if (owner.MaterializeAttemptCount == 1)
                {
                    throw PersistenceTestFailure.Transient(
                        "Simulated transient failure after business SaveChanges(false).");
                }

                return eventBuffer.Materialize(dbContext);
            }

            public void CommitConfirmed(DbContext dbContext)
            {
                eventBuffer.CommitConfirmed(dbContext);
            }
        }
    }

}
