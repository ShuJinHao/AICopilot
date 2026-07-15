using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.DataWorker;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AICopilot.ArchitectureTests;

[Collection("ProcessEnvironment")]
public sealed class DataWorkerCompositionArchitectureTests
{
    [Fact]
    public void AddDataWorkerRuntime_ShouldRegisterWorkerIdentityMaintenanceAndDispatchers()
    {
        const string encryptionKeyVariable = "AICopilotSecurity__ApiKeyEncryptionKey";
        var originalEncryptionKey = Environment.GetEnvironmentVariable(encryptionKeyVariable);
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ai-copilot"] =
                "Host=localhost;Database=architecture;Username=test;Password=test",
            ["AiGateway:Deployment:Mode"] = "SingleInstance",
            ["AiGateway:FinalAgentContextStore:Provider"] = "Memory",
            ["PersistenceMaintenance:IntervalSeconds"] = "17",
            ["PersistenceMaintenance:ReconciliationDelayMinutes"] = "11",
            ["PersistenceMaintenance:MarkerRetentionDays"] = "31",
            ["PersistenceMaintenance:BatchSize"] = "101"
        });

        try
        {
            Environment.SetEnvironmentVariable(
                encryptionKeyVariable,
                "architecture-test-key-do-not-use-outside-tests");
            builder.AddDataWorkerRuntime().Should().BeSameAs(builder);
        }
        finally
        {
            Environment.SetEnvironmentVariable(encryptionKeyVariable, originalEncryptionKey);
        }

        AssertSingleHostedService<OutboxDispatcher>(builder.Services);
        AssertSingleHostedService<AgentTaskRunQueueWorker>(builder.Services);
        AssertSingleHostedService<PersistenceMaintenanceWorker>(builder.Services);

        using (var provider = builder.Services.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser>();
            currentUser.UserName.Should().Be("data-worker");
            currentUser.IdentityProvider.Should().Be("Worker");
            currentUser.IsAuthenticated.Should().BeFalse();
            currentUser.Id.Should().BeNull();
            currentUser.Role.Should().BeNull();
            currentUser.CloudTenantId.Should().BeNull();
            currentUser.CloudEmployeeNo.Should().BeNull();
            currentUser.CloudDepartmentId.Should().BeNull();
            currentUser.CloudDepartmentName.Should().BeNull();
            currentUser.CloudStatusVersion.Should().BeNull();

            scope.ServiceProvider.GetRequiredService<IOptions<PersistenceMaintenanceOptions>>().Value
                .Should().BeEquivalentTo(new PersistenceMaintenanceOptions
                {
                    IntervalSeconds = 17,
                    ReconciliationDelayMinutes = 11,
                    MarkerRetentionDays = 31,
                    BatchSize = 101
                });
        }

        builder.Configuration["PersistenceMaintenance:IntervalSeconds"] = "9";
        using var invalidProvider = builder.Services.BuildServiceProvider();
        var resolveInvalidOptions = () =>
            invalidProvider.GetRequiredService<IOptions<PersistenceMaintenanceOptions>>().Value;
        resolveInvalidOptions.Should().Throw<OptionsValidationException>()
            .WithMessage("*IntervalSeconds must be between 10 and 86400*");
    }

    private static void AssertSingleHostedService<THostedService>(
        IServiceCollection services)
        where THostedService : class, IHostedService
    {
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(THostedService));
    }
}
