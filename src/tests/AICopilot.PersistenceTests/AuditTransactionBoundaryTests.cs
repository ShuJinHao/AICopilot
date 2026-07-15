using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Ids;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.EntityFrameworkCore.Repository;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.SharedKernel.Ai;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AICopilot.PersistenceTests;

[Collection(PostgresPersistenceTestCollection.Name)]
public sealed class AuditTransactionBoundaryTests(PostgresPersistenceFixture fixture)
{
    [Fact]
    public async Task AiGatewayRepository_ShouldCommitBusinessAndAuditRowsTogether()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(fixture.ConnectionString, "aicopilot_audit");
        await MigrateAiGatewayStoreAsync(database.ConnectionString);

        await using var dbContext = new AiGatewayDbContext(CreateOptions<AiGatewayDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.AiGateway));
        await using var auditDbContext = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        var persistence = CreatePersistence(database.ConnectionString, auditDbContext);
        var repository = new AiGatewayRepository<LanguageModel>(
            dbContext,
            persistence.Committer);
        var auditLogWriter = new AuditLogWriter(auditDbContext, persistence.Engine);
        var model = new LanguageModel(
            "OpenAI",
            "audit-txn-model",
            "https://example.local",
            null,
            new ModelParameters { MaxTokens = 1024, Temperature = 0.7f });

        repository.Add(model);
        await auditLogWriter.WriteAsync(CreateAuditRequest(model.Id.ToString(), model.Name));

        await repository.SaveChangesAsync();

        await using var verifyGateway = new AiGatewayDbContext(CreateOptions<AiGatewayDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.AiGateway));
        await using var verifyAudit = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        (await verifyGateway.LanguageModels.AnyAsync(item => item.Id == model.Id)).Should().BeTrue();
        (await verifyAudit.AuditLogs.AnyAsync(item => item.TargetId == model.Id.ToString())).Should().BeTrue();
    }

    [Fact]
    public async Task AiGatewayRepository_ShouldCommitDomainEventOutboxAuditAndMarkerTogether()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_audit");
        await MigrateAiGatewayStoreAsync(database.ConnectionString);

        await using var dbContext = new AiGatewayDbContext(CreateOptions<AiGatewayDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.AiGateway));
        await using var auditDbContext = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        var persistence = CreatePersistence(database.ConnectionString, auditDbContext);
        var repository = new AiGatewayRepository<Session>(dbContext, persistence.Committer);
        var auditLogWriter = new AuditLogWriter(auditDbContext, persistence.Engine);
        var session = new Session(Guid.NewGuid(), ConversationTemplateId.New());
        session.AddMessage("atomic outbox test", MessageType.User);

        repository.Add(session);
        await auditLogWriter.WriteAsync(CreateAuditRequest(session.Id.ToString(), session.Title));
        await repository.SaveChangesAsync();

        await using var verifyGateway = new AiGatewayDbContext(CreateOptions<AiGatewayDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.AiGateway));
        await using var verifyAudit = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        await using var verifyOutbox = new OutboxDbContext(
            new DbContextOptionsBuilder<OutboxDbContext>()
                .UseNpgsql(database.ConnectionString)
                .Options);
        await using var verifyMarkers = new PersistenceCommitMarkerDbContext(
            PostgresPersistenceTestOptions.CreateMarker(database.ConnectionString));

        (await verifyGateway.Sessions.AnyAsync(item => item.Id == session.Id)).Should().BeTrue();
        (await verifyGateway.Messages.AnyAsync(item => item.SessionId == session.Id)).Should().BeTrue();
        (await verifyAudit.AuditLogs.AnyAsync(item => item.TargetId == session.Id.ToString())).Should().BeTrue();
        (await verifyOutbox.OutboxMessages.SingleAsync()).EventTypeName
            .Should().Be(typeof(MessageAddedToSessionEvent).FullName);
        (await verifyMarkers.CommitMarkers.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RepositorySave_ShouldNotWriteMarker_WhenNothingIsPending()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_audit");
        await MigrateAiGatewayStoreAsync(database.ConnectionString);

        await using var dbContext = new AiGatewayDbContext(CreateOptions<AiGatewayDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.AiGateway));
        await using var auditDbContext = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        var persistence = CreatePersistence(database.ConnectionString, auditDbContext);
        var repository = new AiGatewayRepository<LanguageModel>(dbContext, persistence.Committer);

        (await repository.SaveChangesAsync()).Should().Be(0);

        await using var verifyMarkers = new PersistenceCommitMarkerDbContext(
            PostgresPersistenceTestOptions.CreateMarker(database.ConnectionString));
        (await verifyMarkers.CommitMarkers.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AiGatewayRepository_ShouldRollbackBusinessRows_WhenAuditSaveFails()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(fixture.ConnectionString, "aicopilot_audit");
        await MigrateAiGatewayStoreAsync(database.ConnectionString);

        await using var dbContext = new AiGatewayDbContext(CreateOptions<AiGatewayDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.AiGateway));
        await using var auditDbContext = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        var persistence = CreatePersistence(database.ConnectionString, auditDbContext);
        var repository = new AiGatewayRepository<LanguageModel>(
            dbContext,
            persistence.Committer);
        var auditLogWriter = new AuditLogWriter(auditDbContext, persistence.Engine);
        var model = new LanguageModel(
            "OpenAI",
            "audit-txn-rollback-model",
            "https://example.local",
            null,
            new ModelParameters { MaxTokens = 1024, Temperature = 0.7f });

        repository.Add(model);
        await auditLogWriter.WriteAsync(CreateInvalidAuditRequest(model.Id.ToString(), model.Name));

        Func<Task> act = async () => await repository.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
        await using var verifyGateway = new AiGatewayDbContext(CreateOptions<AiGatewayDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.AiGateway));
        await using var verifyAudit = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        (await verifyGateway.LanguageModels.AnyAsync(item => item.Id == model.Id)).Should().BeFalse();
        (await verifyAudit.AuditLogs.AnyAsync(item => item.TargetId == model.Id.ToString())).Should().BeFalse();
    }

    [Fact]
    public async Task BusinessDatabaseRepository_ShouldRollbackCreateUpdateAndDelete_WhenAuditSaveFails()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(fixture.ConnectionString, "aicopilot_audit");
        await MigrateDataAnalysisStoreAsync(database.ConnectionString);

        var createRollback = await SaveBusinessDatabaseWithInvalidAuditAsync(
            database.ConnectionString,
            repository =>
            {
                var entity = CreateBusinessDatabase("audit-txn-create-rollback");
                repository.Add(entity);
                return entity;
            });
        createRollback.Should().BeFalse();
        (await BusinessDatabaseExistsAsync(database.ConnectionString, "audit-txn-create-rollback")).Should().BeFalse();

        var entityId = await CreateBusinessDatabaseWithValidAuditAsync(database.ConnectionString, "audit-txn-existing");

        var updateRollback = await SaveBusinessDatabaseWithInvalidAuditAsync(
            database.ConnectionString,
            async repository =>
            {
                var entity = await repository.GetByIdAsync(new BusinessDatabaseId(entityId));
                entity.Should().NotBeNull();
                entity!.UpdateInfo("audit-txn-updated", "updated description");
                repository.Update(entity);
                return entity;
            });
        updateRollback.Should().BeFalse();
        (await BusinessDatabaseExistsAsync(database.ConnectionString, "audit-txn-existing")).Should().BeTrue();
        (await BusinessDatabaseExistsAsync(database.ConnectionString, "audit-txn-updated")).Should().BeFalse();

        var deleteRollback = await SaveBusinessDatabaseWithInvalidAuditAsync(
            database.ConnectionString,
            async repository =>
            {
                var entity = await repository.GetByIdAsync(new BusinessDatabaseId(entityId));
                entity.Should().NotBeNull();
                repository.Delete(entity!);
                return entity!;
            });
        deleteRollback.Should().BeFalse();
        (await BusinessDatabaseExistsAsync(database.ConnectionString, "audit-txn-existing")).Should().BeTrue();
    }

    [Fact]
    public async Task RagRepository_ShouldCommitBusinessAndAuditRowsTogether()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(fixture.ConnectionString, "aicopilot_audit");
        await MigrateRagStoreAsync(database.ConnectionString);

        await using var dbContext = new RagDbContext(CreateOptions<RagDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.Rag));
        await using var auditDbContext = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        var persistence = CreatePersistence(database.ConnectionString, auditDbContext);
        var repository = new RagRepository<EmbeddingModel>(
            dbContext,
            persistence.Committer);
        var auditLogWriter = new AuditLogWriter(auditDbContext, persistence.Engine);
        var model = CreateEmbeddingModel("audit-txn-rag-model");

        repository.Add(model);
        await auditLogWriter.WriteAsync(CreateAuditRequest(model.Id.ToString(), model.Name));
        await repository.SaveChangesAsync();

        await using var verifyRag = new RagDbContext(CreateOptions<RagDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.Rag));
        await using var verifyAudit = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        (await verifyRag.EmbeddingModels.AnyAsync(item => item.Id == model.Id)).Should().BeTrue();
        (await verifyAudit.AuditLogs.AnyAsync(item => item.TargetId == model.Id.ToString())).Should().BeTrue();
    }

    [Fact]
    public async Task RagRepository_ShouldRollbackBusinessRows_WhenAuditSaveFails()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(fixture.ConnectionString, "aicopilot_audit");
        await MigrateRagStoreAsync(database.ConnectionString);

        await using var dbContext = new RagDbContext(CreateOptions<RagDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.Rag));
        await using var auditDbContext = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        var persistence = CreatePersistence(database.ConnectionString, auditDbContext);
        var repository = new RagRepository<EmbeddingModel>(
            dbContext,
            persistence.Committer);
        var auditLogWriter = new AuditLogWriter(auditDbContext, persistence.Engine);
        var model = CreateEmbeddingModel("audit-txn-rag-rollback");

        repository.Add(model);
        await auditLogWriter.WriteAsync(CreateInvalidAuditRequest(model.Id.ToString(), model.Name));
        Func<Task> act = async () => await repository.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
        await using var verifyRag = new RagDbContext(CreateOptions<RagDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.Rag));
        await using var verifyAudit = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        (await verifyRag.EmbeddingModels.AnyAsync(item => item.Id == model.Id)).Should().BeFalse();
        (await verifyAudit.AuditLogs.AnyAsync(item => item.TargetId == model.Id.ToString())).Should().BeFalse();
    }

    [Fact]
    public async Task McpServerRepository_ShouldRollbackBusinessRows_WhenAuditSaveFails()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(fixture.ConnectionString, "aicopilot_audit");
        await MigrateMcpServerStoreAsync(database.ConnectionString);

        await using var dbContext = new McpServerDbContext(CreateOptions<McpServerDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.McpServer));
        await using var auditDbContext = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        var persistence = CreatePersistence(database.ConnectionString, auditDbContext);
        var repository = new McpServerRepository(
            dbContext,
            persistence.Committer);
        var auditLogWriter = new AuditLogWriter(auditDbContext, persistence.Engine);
        var server = CreateMcpServer("audit-txn-mcp-rollback");

        repository.Add(server);
        await auditLogWriter.WriteAsync(CreateInvalidAuditRequest(server.Id.ToString(), server.Name));
        Func<Task> act = async () => await repository.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
        await using var verifyMcp = new McpServerDbContext(CreateOptions<McpServerDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.McpServer));
        await using var verifyAudit = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        (await verifyMcp.McpServerInfos.AnyAsync(item => item.Id == server.Id)).Should().BeFalse();
        (await verifyAudit.AuditLogs.AnyAsync(item => item.TargetId == server.Id.ToString())).Should().BeFalse();
    }

    [Fact]
    public async Task McpServerRepository_ShouldCommitBusinessAndAuditRowsTogether()
    {
        await using var database = await PostgresScratchDatabase.CreateAsync(fixture.ConnectionString, "aicopilot_audit");
        await MigrateMcpServerStoreAsync(database.ConnectionString);

        await using var dbContext = new McpServerDbContext(CreateOptions<McpServerDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.McpServer));
        await using var auditDbContext = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        var persistence = CreatePersistence(database.ConnectionString, auditDbContext);
        var repository = new McpServerRepository(
            dbContext,
            persistence.Committer);
        var auditLogWriter = new AuditLogWriter(auditDbContext, persistence.Engine);
        var server = CreateMcpServer("audit-txn-mcp-server");

        repository.Add(server);
        await auditLogWriter.WriteAsync(CreateAuditRequest(server.Id.ToString(), server.Name));
        await repository.SaveChangesAsync();

        await using var verifyMcp = new McpServerDbContext(CreateOptions<McpServerDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.McpServer));
        await using var verifyAudit = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        (await verifyMcp.McpServerInfos.AnyAsync(item => item.Id == server.Id)).Should().BeTrue();
        (await verifyAudit.AuditLogs.AnyAsync(item => item.TargetId == server.Id.ToString())).Should().BeTrue();
    }

    private static async Task<bool> SaveBusinessDatabaseWithInvalidAuditAsync(
        string connectionString,
        Func<BusinessDatabaseRepository, BusinessDatabase> arrange)
    {
        return await SaveBusinessDatabaseWithInvalidAuditAsync(
            connectionString,
            repository => Task.FromResult(arrange(repository)));
    }

    private static async Task<bool> SaveBusinessDatabaseWithInvalidAuditAsync(
        string connectionString,
        Func<BusinessDatabaseRepository, Task<BusinessDatabase>> arrange)
    {
        await using var dbContext = new DataAnalysisDbContext(CreateOptions<DataAnalysisDbContext>(
            connectionString,
            MigrationHistoryTables.DataAnalysis));
        await using var auditDbContext = new AuditDbContext(CreateAuditOptions(connectionString));
        var persistence = CreatePersistence(connectionString, auditDbContext);
        var repository = new BusinessDatabaseRepository(
            dbContext,
            persistence.Committer);
        var auditLogWriter = new AuditLogWriter(auditDbContext, persistence.Engine);
        var entity = await arrange(repository);

        await auditLogWriter.WriteAsync(CreateInvalidAuditRequest(entity.Id.ToString(), entity.Name));
        Func<Task> act = async () => await repository.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
        return false;
    }

    private static async Task<Guid> CreateBusinessDatabaseWithValidAuditAsync(string connectionString, string name)
    {
        await using var dbContext = new DataAnalysisDbContext(CreateOptions<DataAnalysisDbContext>(
            connectionString,
            MigrationHistoryTables.DataAnalysis));
        await using var auditDbContext = new AuditDbContext(CreateAuditOptions(connectionString));
        var persistence = CreatePersistence(connectionString, auditDbContext);
        var repository = new BusinessDatabaseRepository(
            dbContext,
            persistence.Committer);
        var auditLogWriter = new AuditLogWriter(auditDbContext, persistence.Engine);
        var entity = CreateBusinessDatabase(name);

        repository.Add(entity);
        await auditLogWriter.WriteAsync(CreateAuditRequest(entity.Id.ToString(), entity.Name));
        await repository.SaveChangesAsync();
        return entity.Id.Value;
    }

    private static async Task<bool> BusinessDatabaseExistsAsync(string connectionString, string name)
    {
        await using var dbContext = new DataAnalysisDbContext(CreateOptions<DataAnalysisDbContext>(
            connectionString,
            MigrationHistoryTables.DataAnalysis));
        return await dbContext.BusinessDatabases.AnyAsync(item => item.Name == name);
    }

    private static BusinessDatabase CreateBusinessDatabase(string name)
    {
        return new BusinessDatabase(
            name,
            "transaction boundary test database",
            "Host=localhost;Database=readonly;Username=reader;Password=fake-test-only",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            externalSystemType: BusinessDataExternalSystemType.NonCloud,
            readOnlyCredentialVerified: true);
    }

    private static EmbeddingModel CreateEmbeddingModel(string name)
    {
        return new EmbeddingModel(
            name,
            "OpenAI",
            "https://embedding.example",
            "text-embedding-3-small",
            1536,
            8191,
            "redacted");
    }

    private static McpServerInfo CreateMcpServer(string name)
    {
        return new McpServerInfo(
            name,
            "transaction boundary test server",
            McpTransportType.Stdio,
            "dotnet",
            "server.dll --token redacted",
            ChatExposureMode.Advisory,
            [new McpAllowedTool("Echo")],
            true);
    }

    private static AuditLogWriteRequest CreateAuditRequest(string targetId, string targetName)
    {
        return new AuditLogWriteRequest(
            AuditActionGroups.Config,
            "AuditTransactionBoundary.Test",
            "AuditTransactionBoundary",
            targetId,
            targetName,
            AuditResults.Succeeded,
            $"Committed audit transaction test for {targetName}.");
    }

    private static AuditLogWriteRequest CreateInvalidAuditRequest(string targetId, string targetName)
    {
        return new AuditLogWriteRequest(
            new string('x', 65),
            "AuditTransactionBoundary.Test",
            "AuditTransactionBoundary",
            targetId,
            targetName,
            AuditResults.Succeeded,
            $"Rejected audit transaction test for {targetName}.");
    }

    private static PersistenceServices CreatePersistence(
        string connectionString,
        AuditDbContext auditDbContext)
    {
        var engine = new PersistenceCommitEngine(
            PostgresPersistenceTestOptions.CreateMarker(connectionString));
        var committer = new RepositoryPersistenceCommitter(
            auditDbContext,
            engine,
            [new AiGatewayDomainEventOutboxSource(), new RagIntegrationEventBuffer()],
            new PersistenceCommitScope());
        return new PersistenceServices(engine, committer);
    }

    private sealed record PersistenceServices(
        PersistenceCommitEngine Engine,
        RepositoryPersistenceCommitter Committer);

    private static async Task MigrateAiGatewayStoreAsync(string connectionString)
    {
        await using var aiCopilot = new AiCopilotDbContext(CreateOptions<AiCopilotDbContext>(
            connectionString,
            MigrationHistoryTables.AiCopilot));
        await aiCopilot.Database.MigrateAsync();

        await using var aiGateway = new AiGatewayDbContext(CreateOptions<AiGatewayDbContext>(
            connectionString,
            MigrationHistoryTables.AiGateway));
        await aiGateway.Database.MigrateAsync();
    }

    private static async Task MigrateDataAnalysisStoreAsync(string connectionString)
    {
        await using var aiCopilot = new AiCopilotDbContext(CreateOptions<AiCopilotDbContext>(
            connectionString,
            MigrationHistoryTables.AiCopilot));
        await aiCopilot.Database.MigrateAsync();

        await using var dataAnalysis = new DataAnalysisDbContext(CreateOptions<DataAnalysisDbContext>(
            connectionString,
            MigrationHistoryTables.DataAnalysis));
        await dataAnalysis.Database.MigrateAsync();
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

    private static async Task MigrateMcpServerStoreAsync(string connectionString)
    {
        await using var aiCopilot = new AiCopilotDbContext(CreateOptions<AiCopilotDbContext>(
            connectionString,
            MigrationHistoryTables.AiCopilot));
        await aiCopilot.Database.MigrateAsync();

        await using var mcpServer = new McpServerDbContext(CreateOptions<McpServerDbContext>(
            connectionString,
            MigrationHistoryTables.McpServer));
        await mcpServer.Database.MigrateAsync();
    }

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

}
