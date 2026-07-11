using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.EntityFrameworkCore.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AICopilot.BackendTests;

internal static class IdentityPersistenceTestSupport
{
    public static async Task<PostgresScratchDatabase> CreateMigratedDatabaseAsync(
        PostgresPersistenceFixture fixture)
    {
        var database = await PostgresScratchDatabase.CreateAsync(
            fixture.ConnectionString,
            "aicopilot_identity_commit");
        try
        {
            await using var root = new AiCopilotDbContext(
                PostgresPersistenceTestOptions.Create<AiCopilotDbContext>(
                    database.ConnectionString,
                    MigrationHistoryTables.AiCopilot));
            await root.Database.MigrateAsync();
            await using var identity = new IdentityStoreDbContext(
                CreateIdentityOptions(database.ConnectionString));
            await identity.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public static IdentityTransactionalExecutionService CreateService(
        string connectionString,
        IdentityStoreDbContext dbContext)
    {
        return new IdentityTransactionalExecutionService(
            dbContext,
            new PersistenceCommitEngine(
                PostgresPersistenceTestOptions.CreateMarker(connectionString)));
    }

    public static DbContextOptions<IdentityStoreDbContext> CreateIdentityOptions(
        string connectionString,
        params IInterceptor[] interceptors)
    {
        var history = MigrationHistoryTables.IdentityStore;
        var builder = new DbContextOptionsBuilder<IdentityStoreDbContext>()
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

        return builder.Options;
    }

    public static async Task AssertMarkerCountAsync(
        string connectionString,
        int expected)
    {
        await using var markers = new PersistenceCommitMarkerDbContext(
            PostgresPersistenceTestOptions.CreateMarker(connectionString));
        (await markers.CommitMarkers.CountAsync()).Should().Be(expected);
    }
}
