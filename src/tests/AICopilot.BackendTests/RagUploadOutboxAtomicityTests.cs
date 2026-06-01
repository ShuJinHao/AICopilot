using System.Text.Json;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.EntityFrameworkCore.Repository;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.RagService.Commands.Documents;
using AICopilot.RagService.Documents;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace AICopilot.BackendTests;

[Collection(CoreBackendTestCollection.Name)]
public sealed class RagUploadOutboxAtomicityTests(CoreAICopilotAppFixture fixture)
{
    [Fact]
    public async Task UploadDocument_ShouldCommitDocumentAndOutboxMessageTogether()
    {
        await using var database = await ScratchDatabase.CreateAsync(await fixture.GetConnectionStringAsync());
        await MigrateRagStoreAsync(database.ConnectionString);
        var knowledgeBaseId = await SeedKnowledgeBaseAsync(database.ConnectionString);

        await using var dbContext = new RagDbContext(
            CreateOptions<RagDbContext>(database.ConnectionString, MigrationHistoryTables.Rag));
        await using var auditDbContext = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        var repository = new RagRepository<KnowledgeBase>(
            dbContext,
            new AuditTransactionCoordinator(auditDbContext));
        var stager = new RagIntegrationEventStager(dbContext);
        var handler = new UploadDocumentCommandHandler(
            repository,
            new CapturingFileStorage(),
            new FixedDocumentFormatPolicy([".txt"]),
            stager,
            new AuditLogWriter(auditDbContext),
            new TestCurrentUser(role: "Admin"),
            NullLogger<UploadDocumentCommandHandler>.Instance);

        var result = await handler.Handle(
            new UploadDocumentCommand(
                knowledgeBaseId,
                new FileUploadStream("atomic.txt", new MemoryStream([1, 2, 3]))),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await using var verifyContext = new RagDbContext(
            CreateOptions<RagDbContext>(database.ConnectionString, MigrationHistoryTables.Rag));
        var document = await verifyContext.Documents.SingleAsync();
        document.Id.Value.Should().Be(result.Value!.Id);
        var outboxMessage = await verifyContext.OutboxMessages.SingleAsync();
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
    }

    [Fact]
    public async Task UploadDocument_ShouldNotCommitDocumentOrOutbox_WhenStagingFails()
    {
        await using var database = await ScratchDatabase.CreateAsync(await fixture.GetConnectionStringAsync());
        await MigrateRagStoreAsync(database.ConnectionString);
        var knowledgeBaseId = await SeedKnowledgeBaseAsync(database.ConnectionString);
        var fileStorage = new CapturingFileStorage();

        await using var dbContext = new RagDbContext(
            CreateOptions<RagDbContext>(database.ConnectionString, MigrationHistoryTables.Rag));
        await using var auditDbContext = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        var repository = new RagRepository<KnowledgeBase>(
            dbContext,
            new AuditTransactionCoordinator(auditDbContext));
        var handler = new UploadDocumentCommandHandler(
            repository,
            fileStorage,
            new FixedDocumentFormatPolicy([".txt"]),
            new ThrowingEventStager(new InvalidOperationException("outbox staging failed")),
            new AuditLogWriter(auditDbContext),
            new TestCurrentUser(role: "Admin"),
            NullLogger<UploadDocumentCommandHandler>.Instance);

        Func<Task> act = async () => await handler.Handle(
            new UploadDocumentCommand(
                knowledgeBaseId,
                new FileUploadStream("staging-failure.txt", new MemoryStream([1, 2, 3]))),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("outbox staging failed");
        fileStorage.DeleteCount.Should().Be(1);
        fileStorage.DeletedPaths.Should().ContainSingle().Which.Should().Be("staging-failure.txt");

        await using var verifyContext = new RagDbContext(
            CreateOptions<RagDbContext>(database.ConnectionString, MigrationHistoryTables.Rag));
        (await verifyContext.Documents.AnyAsync()).Should().BeFalse();
        (await verifyContext.OutboxMessages.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteDocument_ShouldCommitAggregateAuditAndFileDeletionOutboxTogether()
    {
        await using var database = await ScratchDatabase.CreateAsync(await fixture.GetConnectionStringAsync());
        await MigrateRagStoreAsync(database.ConnectionString);
        var seed = await SeedKnowledgeBaseWithDocumentAsync(database.ConnectionString);

        await using var dbContext = new RagDbContext(
            CreateOptions<RagDbContext>(database.ConnectionString, MigrationHistoryTables.Rag));
        await using var auditDbContext = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        var repository = new RagRepository<KnowledgeBase>(
            dbContext,
            new AuditTransactionCoordinator(auditDbContext));
        var handler = new DeleteDocumentCommandHandler(
            repository,
            new RagIntegrationEventStager(dbContext),
            new AuditLogWriter(auditDbContext),
            new TestCurrentUser(role: "Admin"));

        var result = await handler.Handle(new DeleteDocumentCommand(seed.DocumentId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await using var verifyContext = new RagDbContext(
            CreateOptions<RagDbContext>(database.ConnectionString, MigrationHistoryTables.Rag));
        var document = await verifyContext.Documents.SingleAsync();
        document.Status.Should().Be(DocumentStatus.SoftDeleted);
        var outboxMessage = await verifyContext.OutboxMessages.SingleAsync();
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

        await using var verifyAudit = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        var audit = await verifyAudit.AuditLogs.SingleAsync();
        audit.ActionCode.Should().Be("Rag.DeleteDocument");
        audit.TargetId.Should().Be(seed.DocumentId.ToString());
        audit.TargetName.Should().Be(seed.FileName);
    }

    private static async Task MigrateRagStoreAsync(string connectionString)
    {
        await using var aiCopilot = new AiCopilotDbContext(CreateOptions<AiCopilotDbContext>(
            connectionString,
            MigrationHistoryTables.AiCopilot));
        await aiCopilot.Database.MigrateAsync();

        await using var rag = new RagDbContext(CreateOptions<RagDbContext>(
            connectionString,
            MigrationHistoryTables.Rag));
        await rag.Database.MigrateAsync();
    }

    private static async Task<Guid> SeedKnowledgeBaseAsync(string connectionString)
    {
        await using var dbContext = new RagDbContext(CreateOptions<RagDbContext>(
            connectionString,
            MigrationHistoryTables.Rag));
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
        await using var dbContext = new RagDbContext(CreateOptions<RagDbContext>(
            connectionString,
            MigrationHistoryTables.Rag));
        var embeddingModel = new EmbeddingModel(
            "delete-test-embedding",
            "OpenAI",
            "https://example.local",
            "text-embedding-test",
            1536,
            8191);
        var knowledgeBase = new KnowledgeBase("delete-test-kb", "test knowledge base", embeddingModel.Id);
        var document = knowledgeBase.AddDocument(
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

    private sealed record SeededDocument(
        Guid KnowledgeBaseId,
        int DocumentId,
        string FilePath,
        string FileName);

    private static DbContextOptions<TContext> CreateOptions<TContext>(
        string connectionString,
        MigrationHistoryTable historyTable)
        where TContext : DbContext
    {
        return new DbContextOptionsBuilder<TContext>()
            .UseNpgsqlWithMigrationHistory(connectionString, historyTable)
            .Options;
    }

    private static DbContextOptions<AuditDbContext> CreateAuditOptions(string connectionString)
    {
        return new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }

    private sealed class FixedDocumentFormatPolicy(IReadOnlyCollection<string> supportedExtensions) : IDocumentFormatPolicy
    {
        public IReadOnlyCollection<string> SupportedExtensions { get; } = supportedExtensions;

        public bool IsSupported(string extension)
        {
            return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class CapturingFileStorage : IFileStorageService
    {
        public int SaveCount { get; private set; }

        public int DeleteCount { get; private set; }

        public List<string> DeletedPaths { get; } = [];

        public Task<string> SaveAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(fileName);
        }

        public Task<Stream?> GetAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Stream?>(null);
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            DeletedPaths.Add(path);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingEventStager(Exception exception) : IIntegrationEventStager
    {
        public void Stage<TEvent>(TEvent message)
            where TEvent : class
        {
            throw exception;
        }

        public void Stage<TEvent>(Func<TEvent> messageFactory)
            where TEvent : class
        {
            throw exception;
        }
    }

    private sealed class ScratchDatabase : IAsyncDisposable
    {
        private ScratchDatabase(string adminConnectionString, string databaseName, string connectionString)
        {
            AdminConnectionString = adminConnectionString;
            DatabaseName = databaseName;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        private string AdminConnectionString { get; }

        private string DatabaseName { get; }

        public static async Task<ScratchDatabase> CreateAsync(string baseConnectionString)
        {
            var databaseName = $"aicopilot_rag_upload_{Guid.NewGuid():N}";
            var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = "postgres"
            };
            var scratchBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = databaseName
            };

            await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)}";
            await command.ExecuteNonQueryAsync();

            return new ScratchDatabase(adminBuilder.ConnectionString, databaseName, scratchBuilder.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(AdminConnectionString);
            await connection.OpenAsync();

            await using (var terminate = connection.CreateCommand())
            {
                terminate.CommandText = """
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE datname = @database_name
                      AND pid <> pg_backend_pid()
                    """;
                var parameter = terminate.CreateParameter();
                parameter.ParameterName = "database_name";
                parameter.Value = DatabaseName;
                terminate.Parameters.Add(parameter);
                await terminate.ExecuteNonQueryAsync();
            }

            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(DatabaseName)} WITH (FORCE)";
            await drop.ExecuteNonQueryAsync();
        }

        private static string QuoteIdentifier(string value)
        {
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }
    }
}
