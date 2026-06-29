using AICopilot.EntityFrameworkCore;
using AICopilot.Services.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;

namespace AICopilot.MigrationWorkApp;

public class Worker(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    public const string ActivitySourceName = "Migrations";
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("Migrating database", ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AiCopilotDbContext>();
            var identityStoreDbContext = scope.ServiceProvider.GetRequiredService<IdentityStoreDbContext>();
            var aiGatewayDbContext = scope.ServiceProvider.GetRequiredService<AiGatewayDbContext>();
            var ragDbContext = scope.ServiceProvider.GetRequiredService<RagDbContext>();
            var dataAnalysisDbContext = scope.ServiceProvider.GetRequiredService<DataAnalysisDbContext>();
            var mcpServerDbContext = scope.ServiceProvider.GetRequiredService<McpServerDbContext>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var permissionCatalog = scope.ServiceProvider.GetRequiredService<IPermissionCatalog>();
            var identityAccessService = scope.ServiceProvider.GetRequiredService<IIdentityAccessService>();

            var migrationContexts = MigrationWorkerDatabaseMigrator.CreateMigrationContexts(
                dbContext,
                identityStoreDbContext,
                aiGatewayDbContext,
                ragDbContext,
                dataAnalysisDbContext,
                mcpServerDbContext);

            await MigrationWorkerDatabaseMigrator.RunMigrationsAsync(migrationContexts, cancellationToken);
            await MigrationWorkerIdentitySeeder.SeedAsync(
                roleManager,
                userManager,
                permissionCatalog,
                identityAccessService,
                configuration,
                cancellationToken);
            await MigrationWorkerAiGatewaySeeder.SeedDefaultsAsync(aiGatewayDbContext, cancellationToken);
            await MigrationWorkerCloudReadOnlySeeder.EnsureSourceAsync(
                configuration,
                dataAnalysisDbContext,
                cancellationToken);
            await MigrationWorkerCloudSimulationSeeder.EnsureSourceAsync(
                configuration,
                hostEnvironment.EnvironmentName,
                dataAnalysisDbContext,
                cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }
}
