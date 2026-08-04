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
                    await RunAiGatewayMigrationUnderDdlFenceAsync(
                        (AiGatewayDbContext)migrationContext.DbContext,
                        connection,
                        cancellationToken);
                    continue;
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

    internal static async Task AcquireAiGatewaySchemaDdlFenceAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var relations = await ReadSchemaLockTargetsAsync(
            connection,
            """
            SELECT format('%I.%I', namespace_state.nspname, relation_state.relname)
            FROM pg_class AS relation_state
            JOIN pg_namespace AS namespace_state
              ON namespace_state.oid = relation_state.relnamespace
            WHERE namespace_state.nspname = 'aigateway'
              AND relation_state.relkind IN ('r', 'p', 'v', 'm', 'f')
            ORDER BY relation_state.oid;
            """,
            cancellationToken);
        foreach (var relation in relations)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"LOCK TABLE {relation} IN SHARE ROW EXCLUSIVE MODE;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var sequences = await ReadSequenceLockTargetsAsync(connection, cancellationToken);
        foreach (var (sequence, cacheSize) in sequences)
        {
            await using var command = connection.CreateCommand();
            // PostgreSQL does not support LOCK TABLE for sequences. Re-applying the
            // canonical cache value takes a transactional ShareRowExclusiveLock
            // without advancing or resetting the sequence.
            command.CommandText = $"ALTER SEQUENCE {sequence} CACHE {cacheSize};";
            await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static async Task RunAiGatewayMigrationUnderDdlFenceAsync(
        AiGatewayDbContext dbContext,
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                cancellationToken);
            await AcquireAiGatewaySchemaDdlFenceAsync(connection, cancellationToken);

            await AiGatewayProductionUpgradePreflight.InspectAsync(
                connection,
                cancellationToken);
            await dbContext.Database.MigrateAsync(cancellationToken);

            var migrated = await AiGatewayProductionUpgradePreflight.InspectAsync(
                connection,
                cancellationToken);
            if (migrated.State != AiGatewayProductionUpgradeState.Current)
            {
                throw new InvalidOperationException(
                    "AiGateway migration did not produce the frozen current schema/history fingerprint.");
            }

            await transaction.CommitAsync(cancellationToken);
        });
    }

    private static async Task<string[]> ReadSchemaLockTargetsAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var targets = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            targets.Add(reader.GetString(0));
        }

        return targets.ToArray();
    }

    private static async Task<(string Sequence, long CacheSize)[]> ReadSequenceLockTargetsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT format('%I.%I', namespace_state.nspname, sequence_state.relname),
                   sequence_metadata.seqcache
            FROM pg_class AS sequence_state
            JOIN pg_namespace AS namespace_state
              ON namespace_state.oid = sequence_state.relnamespace
            JOIN pg_sequence AS sequence_metadata
              ON sequence_metadata.seqrelid = sequence_state.oid
            WHERE namespace_state.nspname = 'aigateway'
              AND sequence_state.relkind = 'S'
            ORDER BY sequence_state.oid;
            """;
        var targets = new List<(string Sequence, long CacheSize)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            targets.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        return targets.ToArray();
    }
}
