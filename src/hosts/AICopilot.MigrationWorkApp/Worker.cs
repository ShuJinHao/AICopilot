using AICopilot.EntityFrameworkCore;
using AICopilot.IdentityService.Authorization;
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
            var enabledAdminInvariant = scope.ServiceProvider
                .GetRequiredService<EnabledAdminInvariantPolicy>();
            var transactionalExecutionService = scope.ServiceProvider
                .GetRequiredService<ITransactionalExecutionService>();

            if (configuration.GetValue<bool>("MigrationWorker:CheckSecretsOnly"))
            {
                await MigrationWorkerSecretMigrator.VerifyAsync(
                    aiGatewayDbContext,
                    ragDbContext,
                    cancellationToken);
                hostApplicationLifetime.StopApplication();
                return;
            }

            var migrationContexts = MigrationWorkerDatabaseMigrator.CreateMigrationContexts(
                dbContext,
                identityStoreDbContext,
                aiGatewayDbContext,
                ragDbContext,
                dataAnalysisDbContext,
                mcpServerDbContext);

            await MigrationWorkerDatabaseMigrator.RunMigrationsAsync(migrationContexts, cancellationToken);
            await MigrationWorkerSecretMigrator.MigrateAsync(
                aiGatewayDbContext,
                ragDbContext,
                cancellationToken);
            await MigrationWorkerIdentitySeeder.SeedAsync(
                roleManager,
                userManager,
                permissionCatalog,
                identityAccessService,
                enabledAdminInvariant,
                transactionalExecutionService,
                configuration,
                cancellationToken);
            await MigrationWorkerAiGatewaySeeder.SeedDefaultsAsync(aiGatewayDbContext, configuration, cancellationToken);
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
