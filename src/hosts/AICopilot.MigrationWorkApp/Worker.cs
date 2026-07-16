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

            async Task ExecuteStageAsync(
                MigrationWorkerStage stage,
                CancellationToken stageCancellationToken)
            {
                switch (stage)
                {
                    case MigrationWorkerStage.VerifySecrets:
                        await MigrationWorkerSecretMigrator.VerifyAsync(
                            aiGatewayDbContext,
                            ragDbContext,
                            stageCancellationToken);
                        break;

                    case MigrationWorkerStage.MigrateDatabases:
                        var migrationContexts = MigrationWorkerDatabaseMigrator.CreateMigrationContexts(
                            dbContext,
                            identityStoreDbContext,
                            aiGatewayDbContext,
                            ragDbContext,
                            dataAnalysisDbContext,
                            mcpServerDbContext);
                        await MigrationWorkerDatabaseMigrator.RunMigrationsAsync(
                            migrationContexts,
                            stageCancellationToken);
                        break;

                    case MigrationWorkerStage.MigrateSecrets:
                        await MigrationWorkerSecretMigrator.MigrateAsync(
                            aiGatewayDbContext,
                            ragDbContext,
                            stageCancellationToken);
                        break;

                    case MigrationWorkerStage.SeedIdentity:
                        await MigrationWorkerIdentitySeeder.SeedAsync(
                            roleManager,
                            userManager,
                            permissionCatalog,
                            identityAccessService,
                            enabledAdminInvariant,
                            transactionalExecutionService,
                            configuration,
                            stageCancellationToken);
                        break;

                    case MigrationWorkerStage.SeedAiGateway:
                        await MigrationWorkerAiGatewaySeeder.SeedDefaultsAsync(
                            aiGatewayDbContext,
                            configuration,
                            stageCancellationToken);
                        break;

                    case MigrationWorkerStage.SeedCloudReadOnly:
                        await MigrationWorkerCloudReadOnlySeeder.EnsureSourceAsync(
                            configuration,
                            dataAnalysisDbContext,
                            stageCancellationToken);
                        break;

                    case MigrationWorkerStage.SeedSimulation:
                        await MigrationWorkerCloudSimulationSeeder.EnsureSourceAsync(
                            configuration,
                            hostEnvironment.EnvironmentName,
                            dataAnalysisDbContext,
                            stageCancellationToken);
                        break;

                    case MigrationWorkerStage.StopApplication:
                        hostApplicationLifetime.StopApplication();
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown migration worker stage.");
                }
            }

            await MigrationWorkerExecutionPlan.RunAsync(
                configuration.GetValue<bool>("MigrationWorker:CheckSecretsOnly"),
                ExecuteStageAsync,
                cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

    }
}

internal enum MigrationWorkerStage
{
    VerifySecrets,
    MigrateDatabases,
    MigrateSecrets,
    SeedIdentity,
    SeedAiGateway,
    SeedCloudReadOnly,
    SeedSimulation,
    StopApplication
}

internal static class MigrationWorkerExecutionPlan
{
    private static readonly MigrationWorkerStage[] CheckSecretsOnlyStages =
    [
        MigrationWorkerStage.VerifySecrets,
        MigrationWorkerStage.StopApplication
    ];

    private static readonly MigrationWorkerStage[] FullMigrationStages =
    [
        MigrationWorkerStage.MigrateDatabases,
        MigrationWorkerStage.MigrateSecrets,
        MigrationWorkerStage.SeedIdentity,
        MigrationWorkerStage.SeedAiGateway,
        MigrationWorkerStage.SeedCloudReadOnly,
        MigrationWorkerStage.SeedSimulation,
        MigrationWorkerStage.StopApplication
    ];

    internal static async Task RunAsync(
        bool checkSecretsOnly,
        Func<MigrationWorkerStage, CancellationToken, Task> executeStageAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executeStageAsync);

        var stages = checkSecretsOnly ? CheckSecretsOnlyStages : FullMigrationStages;
        foreach (var stage in stages)
        {
            await executeStageAsync(stage, cancellationToken);
        }
    }
}
