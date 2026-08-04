using AICopilot.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;

namespace AICopilot.MigrationWorkApp;

internal static class MigrationWorkerDatabaseMigrator
{
    internal const long AiGatewayProductionUpgradeLockId = 0x4149474154455741L;

    public static MigrationHistoryBootstrapper.MigrationContext[] CreateMigrationContexts(
        AiCopilotDbContext dbContext,
        IdentityStoreDbContext identityStoreDbContext,
        AiGatewayDbContext aiGatewayDbContext,
        RagDbContext ragDbContext,
        DataAnalysisDbContext dataAnalysisDbContext,
        McpServerDbContext mcpServerDbContext)
    {
        return
        [
            new MigrationHistoryBootstrapper.MigrationContext(dbContext, MigrationHistoryTables.AiCopilot),
            new MigrationHistoryBootstrapper.MigrationContext(identityStoreDbContext, MigrationHistoryTables.IdentityStore),
            new MigrationHistoryBootstrapper.MigrationContext(aiGatewayDbContext, MigrationHistoryTables.AiGateway),
            new MigrationHistoryBootstrapper.MigrationContext(ragDbContext, MigrationHistoryTables.Rag),
            new MigrationHistoryBootstrapper.MigrationContext(dataAnalysisDbContext, MigrationHistoryTables.DataAnalysis),
            new MigrationHistoryBootstrapper.MigrationContext(mcpServerDbContext, MigrationHistoryTables.McpServer)
        ];
    }

    public static async Task RunMigrationsAsync(
        IReadOnlyList<MigrationHistoryBootstrapper.MigrationContext> migrationContexts,
        CancellationToken cancellationToken)
    {
        var aiGateway = migrationContexts.Single(context =>
            context.DbContext is AiGatewayDbContext);
        var database = aiGateway.DbContext.Database;
        var connection = database.GetDbConnection() as NpgsqlConnection
            ?? throw new InvalidOperationException(
                "AiGateway production migration requires an Npgsql connection.");
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await database.OpenConnectionAsync(cancellationToken);
        }

        var lockAcquired = false;
        try
        {
            await AcquireAiGatewayProductionUpgradeLockAsync(connection, cancellationToken);
            lockAcquired = true;
            await AiGatewayProductionUpgradePreflight.InspectAsync(
                connection,
                cancellationToken);

            await MigrationHistoryBootstrapper.BootstrapLegacyHistoryAsync(
                migrationContexts,
                cancellationToken);

            foreach (var migrationContext in migrationContexts)
            {
                if (ReferenceEquals(migrationContext, aiGateway))
                {
                    await AiGatewayProductionUpgradePreflight.InspectAsync(
                        connection,
                        cancellationToken);
                }

                await RunMigrationAsync(migrationContext.DbContext, cancellationToken);
            }
        }
        finally
        {
            try
            {
                if (lockAcquired)
                {
                    await ReleaseAiGatewayProductionUpgradeLockAsync(
                        connection,
                        CancellationToken.None);
                }
            }
            finally
            {
                if (openedHere)
                {
                    await database.CloseConnectionAsync();
                }
            }
        }
    }

    internal static async Task AcquireAiGatewayProductionUpgradeLockAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_lock(@lock_id);";
        command.Parameters.AddWithValue("lock_id", AiGatewayProductionUpgradeLockId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task ReleaseAiGatewayProductionUpgradeLockAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_unlock(@lock_id);";
        command.Parameters.AddWithValue("lock_id", AiGatewayProductionUpgradeLockId);
        var released = await command.ExecuteScalarAsync(cancellationToken);
        if (released is not true)
        {
            throw new InvalidOperationException(
                "AiGateway production migration advisory lock release could not be proven.");
        }
    }

    private static async Task RunMigrationAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () => { await dbContext.Database.MigrateAsync(cancellationToken); });
    }
}
