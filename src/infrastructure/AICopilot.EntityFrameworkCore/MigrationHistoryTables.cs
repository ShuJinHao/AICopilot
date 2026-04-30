using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore;

public sealed record MigrationHistoryTable(string ContextName, string Schema, string TableName);

public static class MigrationHistoryTables
{
    public const string LegacySchema = "public";
    public const string LegacyTableName = "__EFMigrationsHistory";

    public static readonly MigrationHistoryTable AiCopilot = new(
        nameof(AiCopilotDbContext),
        "public",
        "__EFMigrationsHistory_AiCopilot");

    public static readonly MigrationHistoryTable IdentityStore = new(
        nameof(IdentityStoreDbContext),
        "identity",
        "__EFMigrationsHistory_IdentityStore");

    public static readonly MigrationHistoryTable AiGateway = new(
        nameof(AiGatewayDbContext),
        "aigateway",
        "__EFMigrationsHistory_AiGateway");

    public static readonly MigrationHistoryTable Rag = new(
        nameof(RagDbContext),
        "rag",
        "__EFMigrationsHistory_Rag");

    public static readonly MigrationHistoryTable DataAnalysis = new(
        nameof(DataAnalysisDbContext),
        "dataanalysis",
        "__EFMigrationsHistory_DataAnalysis");

    public static readonly MigrationHistoryTable McpServer = new(
        nameof(McpServerDbContext),
        "mcp",
        "__EFMigrationsHistory_McpServer");

    public static IReadOnlyList<MigrationHistoryTable> MigratedContexts { get; } =
    [
        AiCopilot,
        IdentityStore,
        AiGateway,
        Rag,
        DataAnalysis,
        McpServer
    ];
}

public static class AICopilotNpgsqlOptions
{
    public static Action<DbContextOptionsBuilder> ConfigureMigrationHistory(MigrationHistoryTable table)
    {
        return options => options.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(table.TableName, table.Schema));
    }

    public static DbContextOptionsBuilder UseNpgsqlWithMigrationHistory(
        this DbContextOptionsBuilder optionsBuilder,
        string connectionString,
        MigrationHistoryTable table)
    {
        return optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable(table.TableName, table.Schema));
    }

    public static DbContextOptionsBuilder<TContext> UseNpgsqlWithMigrationHistory<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string connectionString,
        MigrationHistoryTable table)
        where TContext : DbContext
    {
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable(table.TableName, table.Schema));
        return optionsBuilder;
    }
}
