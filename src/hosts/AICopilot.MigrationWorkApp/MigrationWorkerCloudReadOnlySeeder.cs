using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.EntityFrameworkCore;
using AICopilot.Services.Contracts.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AICopilot.MigrationWorkApp;

internal static class MigrationWorkerCloudReadOnlySeeder
{
    public static async Task EnsureSourceAsync(
        IConfiguration configuration,
        DataAnalysisDbContext dataAnalysisDbContext,
        CancellationToken cancellationToken)
    {
        var options = CloudReadOnlyBusinessDatabaseSeedPolicy.ResolveOptions(configuration);
        if (!options.Enabled)
        {
            return;
        }

        CloudReadOnlyBusinessDatabaseSeedPolicy.ValidateOptions(configuration, options);

        var database = await dataAnalysisDbContext.BusinessDatabases
            .SingleOrDefaultAsync(item => item.Name == options.DatabaseName, cancellationToken);

        if (database is null)
        {
            dataAnalysisDbContext.BusinessDatabases.Add(
                CloudReadOnlyBusinessDatabaseSeedPolicy.CreateBusinessDatabase(options));
        }
        else
        {
            CloudReadOnlyBusinessDatabaseSeedPolicy.UpdateBusinessDatabase(database, options);
        }

        await dataAnalysisDbContext.SaveChangesAsync(cancellationToken);
    }

}
