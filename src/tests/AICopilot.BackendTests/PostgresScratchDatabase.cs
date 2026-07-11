using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AICopilot.BackendTests;

internal sealed class PostgresScratchDatabase : IAsyncDisposable
{
    private PostgresScratchDatabase(
        string adminConnectionString,
        string databaseName,
        string connectionString)
    {
        AdminConnectionString = adminConnectionString;
        DatabaseName = databaseName;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    private string AdminConnectionString { get; }

    private string DatabaseName { get; }

    public static async Task<PostgresScratchDatabase> CreateAsync(
        string baseConnectionString,
        string prefix = "aicopilot_test")
    {
        var normalizedPrefix = new string(prefix
            .Where(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            .ToArray());
        var databaseName = $"{normalizedPrefix}_{Guid.NewGuid():N}";
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

        return new PostgresScratchDatabase(
            adminBuilder.ConnectionString,
            databaseName,
            scratchBuilder.ConnectionString);
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
            terminate.Parameters.AddWithValue("database_name", DatabaseName);
            await terminate.ExecuteNonQueryAsync();
        }

        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(DatabaseName)} WITH (FORCE)";
        await drop.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string value)
    {
        return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }
}

internal static class PostgresPersistenceTestOptions
{
    public static DbContextOptions<TContext> Create<TContext>(
        string connectionString,
        MigrationHistoryTable historyTable)
        where TContext : DbContext
    {
        return new DbContextOptionsBuilder<TContext>()
            .UseNpgsqlWithMigrationHistory(connectionString, historyTable)
            .Options;
    }

    public static DbContextOptions<AuditDbContext> CreateAudit(string connectionString)
    {
        return new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }

    public static DbContextOptions<PersistenceCommitMarkerDbContext> CreateMarker(
        string connectionString,
        params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<PersistenceCommitMarkerDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.EnableRetryOnFailure(2, TimeSpan.Zero, null));
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return builder.Options;
    }
}
