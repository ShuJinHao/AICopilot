using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AICopilot.EntityFrameworkCore;

public static class MigrationHistoryBootstrapper
{
    public sealed record MigrationContext(DbContext DbContext, MigrationHistoryTable HistoryTable);

    private sealed record HistoryRow(string MigrationId, string ProductVersion);

    public static async Task BootstrapLegacyHistoryAsync(
        IReadOnlyCollection<MigrationContext> migrationContexts,
        CancellationToken cancellationToken)
    {
        if (migrationContexts.Count == 0)
        {
            return;
        }

        var connectionString = GetConnectionString(migrationContexts.First().DbContext);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await EnsureHistorySchemasAsync(connection, transaction, migrationContexts, cancellationToken);

        var legacyTable = new MigrationHistoryTable(
            "Legacy",
            MigrationHistoryTables.LegacySchema,
            MigrationHistoryTables.LegacyTableName);

        if (!await HistoryTableExistsAsync(connection, transaction, legacyTable, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var legacyRows = await ReadHistoryRowsAsync(connection, transaction, legacyTable, cancellationToken);
        foreach (var migrationContext in migrationContexts)
        {
            var contextMigrations = migrationContext.DbContext.Database
                .GetMigrations()
                .ToHashSet(StringComparer.Ordinal);
            var legacyRowsForContext = legacyRows
                .Where(row => contextMigrations.Contains(row.MigrationId))
                .ToArray();

            if (legacyRowsForContext.Length == 0)
            {
                continue;
            }

            await EnsureHistoryTableAsync(connection, transaction, migrationContext.HistoryTable, cancellationToken);
            await CopyLegacyRowsAsync(
                connection,
                transaction,
                migrationContext.HistoryTable,
                legacyRowsForContext,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static string GetConnectionString(DbContext dbContext)
    {
        var connectionString = dbContext.Database.GetConnectionString();
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        connectionString = dbContext.Database.GetDbConnection().ConnectionString;
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        throw new InvalidOperationException("Migration history bootstrap requires a configured database connection string.");
    }

    private static async Task EnsureHistorySchemasAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IEnumerable<MigrationContext> migrationContexts,
        CancellationToken cancellationToken)
    {
        foreach (var schema in migrationContexts
                     .Select(context => context.HistoryTable.Schema)
                     .Distinct(StringComparer.Ordinal))
        {
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                $"CREATE SCHEMA IF NOT EXISTS {QuoteIdentifier(schema)}",
                cancellationToken);
        }
    }

    private static async Task CopyLegacyRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        MigrationHistoryTable targetTable,
        IReadOnlyCollection<HistoryRow> legacyRows,
        CancellationToken cancellationToken)
    {
        var targetMigrationIds = await ReadHistoryMigrationIdsAsync(
            connection,
            transaction,
            targetTable,
            cancellationToken);

        if (targetMigrationIds.Count > 0)
        {
            var missingRows = legacyRows
                .Where(row => !targetMigrationIds.Contains(row.MigrationId))
                .Select(row => row.MigrationId)
                .ToArray();

            if (missingRows.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Migration history bootstrap found partial target history for {targetTable.ContextName} in {targetTable.Schema}.{targetTable.TableName}. Missing legacy migrations: {string.Join(", ", missingRows)}. Refusing to continue because replaying old migrations could corrupt schema ownership.");
            }
        }

        foreach (var row in legacyRows)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"""
                 INSERT INTO {HistoryTableSql(targetTable)} ("MigrationId", "ProductVersion")
                 VALUES (@migration_id, @product_version)
                 ON CONFLICT ("MigrationId") DO NOTHING
                 """;
            command.Parameters.AddWithValue("migration_id", row.MigrationId);
            command.Parameters.AddWithValue("product_version", row.ProductVersion);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task EnsureHistoryTableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        MigrationHistoryTable table,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            $"""
             CREATE TABLE IF NOT EXISTS {HistoryTableSql(table)} (
                 "MigrationId" character varying(150) NOT NULL,
                 "ProductVersion" character varying(32) NOT NULL,
                 CONSTRAINT {QuoteIdentifier("PK_" + table.TableName)} PRIMARY KEY ("MigrationId")
             )
             """,
            cancellationToken);
    }

    private static async Task<bool> HistoryTableExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        MigrationHistoryTable table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT to_regclass(@table_name)::text";
        command.Parameters.AddWithValue("table_name", QualifiedRegclassName(table));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull;
    }

    private static async Task<List<HistoryRow>> ReadHistoryRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        MigrationHistoryTable table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
             SELECT "MigrationId", "ProductVersion"
             FROM {HistoryTableSql(table)}
             ORDER BY "MigrationId"
             """;

        var rows = new List<HistoryRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new HistoryRow(reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }

    private static async Task<HashSet<string>> ReadHistoryMigrationIdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        MigrationHistoryTable table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
             SELECT "MigrationId"
             FROM {HistoryTableSql(table)}
             """;

        var migrationIds = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            migrationIds.Add(reader.GetString(0));
        }

        return migrationIds;
    }

    private static async Task ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string HistoryTableSql(MigrationHistoryTable table)
    {
        return $"{QuoteIdentifier(table.Schema)}.{QuoteIdentifier(table.TableName)}";
    }

    private static string QualifiedRegclassName(MigrationHistoryTable table)
    {
        return $"{table.Schema}.{QuoteIdentifier(table.TableName)}";
    }

    private static string QuoteIdentifier(string value)
    {
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
