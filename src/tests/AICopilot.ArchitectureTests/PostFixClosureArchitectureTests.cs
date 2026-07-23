using AICopilot.AiGatewayService;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.Core.AiGateway.Aggregates.RuntimeSettings;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Infrastructure;
using AICopilot.Infrastructure.AiGateway;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AICopilot.ArchitectureTests;

[Collection("ProcessEnvironment")]
public sealed class PostFixClosureArchitectureTests
{
    [Fact]
    public void RuntimeSettingsContractAndModel_ShouldNotExposeRetiredSummaryThreshold()
    {
        var retiredNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "SummaryThresholdMessages",
            "ReservedSummaryThresholdMessages"
        };
        Type[] contractTypes =
        [
            typeof(ChatRuntimeSettings),
            typeof(ChatRuntimeSettingsDto),
            typeof(UpdateChatRuntimeSettingsCommand)
        ];
        var contractViolations = contractTypes
            .SelectMany(type => type.GetProperties().Select(property => $"{type.Name}.{property.Name}"))
            .Where(property => retiredNames.Contains(property[(property.LastIndexOf('.') + 1)..]))
            .ToArray();

        using var context = new AiGatewayDbContext(
            new DbContextOptionsBuilder<AiGatewayDbContext>()
                .UseNpgsql("Host=localhost;Database=architecture;Username=test;Password=test")
                .Options);
        var modelProperties = context.Model.FindEntityType(typeof(ChatRuntimeSettings))!
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        contractViolations.Should().BeEmpty();
        modelProperties.Should().NotContain(retiredNames);
    }

    [Fact]
    public void ProductionCompositionRoot_ShouldReplaceInMemorySessionLockWithPostgreSql()
    {
        const string encryptionKeyVariable = "AICopilotSecurity__ApiKeyEncryptionKey";
        var originalEncryptionKey = Environment.GetEnvironmentVariable(encryptionKeyVariable);
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ai-copilot"] =
                "Host=localhost;Database=architecture;Username=test;Password=test",
            ["AiGateway:Deployment:Mode"] = "SingleInstance",
            ["AiGateway:FinalAgentContextStore:Provider"] = "Memory"
        });

        builder.AddAiGatewayService();
        builder.Services.Last(descriptor => descriptor.ServiceType == typeof(ISessionExecutionLock))
            .ImplementationType.Should().Be<InMemorySessionExecutionLock>();

        try
        {
            Environment.SetEnvironmentVariable(
                encryptionKeyVariable,
                "architecture-test-key-do-not-use-outside-tests");
            builder.AddInfrastructures();
        }
        finally
        {
            Environment.SetEnvironmentVariable(encryptionKeyVariable, originalEncryptionKey);
        }

        var productionDescriptor = builder.Services
            .Last(descriptor => descriptor.ServiceType == typeof(ISessionExecutionLock));
        productionDescriptor.ImplementationFactory.Should().NotBeNull();

        using var provider = builder.Services.BuildServiceProvider();
        builder.Services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IRepositoryPersistenceAttemptValidator));
        builder.Services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IAgentTaskPlanFreshReadVerifier) &&
            descriptor.ImplementationType == typeof(AICopilot.EntityFrameworkCore.Repository.AgentTaskPlanFreshReadVerifier) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        productionDescriptor.ImplementationFactory!(provider)
            .Should().BeOfType<PostgreSqlSessionExecutionLock>();
    }

    [Fact]
    public void ProductionCompositionRoot_ShouldExposeOnlyCanonicalPlanIntegrityBoundary()
    {
        const string planCompilerContract =
            "AICopilot.AiGatewayService.AgentTasks.IAgentPlanCompiler";
        const string canonicalPlanCompiler =
            "AICopilot.AiGatewayService.AgentTasks.DeterministicAgentPlanCompiler";
        var builder = Host.CreateApplicationBuilder();

        builder.AddAiGatewayService();

        builder.Services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IAgentPlanIntegrityValidator) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        builder.Services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IAgentTaskPlanPersistencePolicy) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        builder.Services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(AgentPlanDraftContractAuthority) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        builder.Services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(AgentTaskPlanFreshReadGate) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        builder.Services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType.FullName == planCompilerContract &&
            descriptor.ImplementationType != null &&
            descriptor.ImplementationType.FullName == canonicalPlanCompiler &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        builder.Services.Should().NotContain(descriptor =>
            descriptor.ServiceType.FullName != planCompilerContract &&
            descriptor.ServiceType.Name.Contains("PlanCompiler", StringComparison.Ordinal) ||
            descriptor.ImplementationType != null &&
            descriptor.ImplementationType.FullName != canonicalPlanCompiler &&
            descriptor.ImplementationType.Name.Contains("PlanCompiler", StringComparison.Ordinal));

        using var provider = builder.Services.BuildServiceProvider();
        var productionValidator = provider.GetRequiredService<IAgentPlanIntegrityValidator>();
        productionValidator.GetType().FullName.Should().Be(
            "AICopilot.AiGatewayService.AgentTasks.AgentPlanCanonicalizer");
        ReferenceEquals(
                productionValidator.GetType().Assembly,
                typeof(AgentPlanDraftContractAuthority).Assembly)
            .Should().BeTrue();

        var productionReferences = typeof(AICopilot.HttpApi.HttpApiCorsConfiguration)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();
        productionReferences.Should().NotContain("Microsoft.AspNetCore.Mvc.Testing");
        productionReferences.Should().NotContain("AICopilot.AgentWorkflowTestKit");
    }

}

[CollectionDefinition("ProcessEnvironment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection;
