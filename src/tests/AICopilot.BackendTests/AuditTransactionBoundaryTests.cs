using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Ids;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Repository;
using AICopilot.EntityFrameworkCore.Transactions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AICopilot.BackendTests;

[Collection(CoreBackendTestCollection.Name)]
public sealed class AuditTransactionBoundaryTests(CoreAICopilotAppFixture fixture)
{
    [Fact]
    public async Task AiGatewayRepository_ShouldCommitBusinessAndAuditRowsTogether()
    {
        await using var database = await ScratchDatabase.CreateAsync(await fixture.GetConnectionStringAsync());
        await MigrateAiGatewayStoreAsync(database.ConnectionString);

        await using var dbContext = new AiGatewayDbContext(CreateOptions<AiGatewayDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.AiGateway));
        await using var auditDbContext = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        var repository = new AiGatewayRepository<LanguageModel>(
            dbContext,
            new AuditTransactionCoordinator(auditDbContext));
        var auditLogWriter = new AuditLogWriter(auditDbContext);
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
    public async Task AiGatewayRepository_ShouldRollbackBusinessRows_WhenAuditSaveFails()
    {
        await using var database = await ScratchDatabase.CreateAsync(await fixture.GetConnectionStringAsync());
        await MigrateAiGatewayStoreAsync(database.ConnectionString);

        await using var dbContext = new AiGatewayDbContext(CreateOptions<AiGatewayDbContext>(
            database.ConnectionString,
            MigrationHistoryTables.AiGateway));
        await using var auditDbContext = new AuditDbContext(CreateAuditOptions(database.ConnectionString));
        var repository = new AiGatewayRepository<LanguageModel>(
            dbContext,
            new AuditTransactionCoordinator(auditDbContext));
        var auditLogWriter = new AuditLogWriter(auditDbContext);
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
        await using var database = await ScratchDatabase.CreateAsync(await fixture.GetConnectionStringAsync());
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
        var repository = new BusinessDatabaseRepository(
            dbContext,
            new AuditTransactionCoordinator(auditDbContext));
        var auditLogWriter = new AuditLogWriter(auditDbContext);
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
        var repository = new BusinessDatabaseRepository(
            dbContext,
            new AuditTransactionCoordinator(auditDbContext));
        var auditLogWriter = new AuditLogWriter(auditDbContext);
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
            "Host=localhost;Database=readonly;Username=reader;Password=secret",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            externalSystemType: BusinessDataExternalSystemType.NonCloud,
            readOnlyCredentialVerified: true);
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
            var databaseName = $"aicopilot_audit_tx_{Guid.NewGuid():N}";
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
