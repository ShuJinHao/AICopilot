using AICopilot.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.MigrationWorkApp;

internal static class MigrationWorkerDatabaseMigrator
{
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
        await MigrationHistoryBootstrapper.BootstrapLegacyHistoryAsync(migrationContexts, cancellationToken);

        foreach (var migrationContext in migrationContexts)
        {
            await RunMigrationAsync(migrationContext.DbContext, cancellationToken);
        }
    }

    private static async Task RunMigrationAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () => { await dbContext.Database.MigrateAsync(cancellationToken); });
    }
}
