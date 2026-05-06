using AICopilot.Infrastructure;
using AICopilot.Infrastructure.AiGateway;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Reflection;

namespace AICopilot.BackendTests;

public sealed class FinalAgentContextDeploymentTests
{
    [Fact]
    public void AddInfrastructures_ShouldRejectMultiInstanceWithoutRedisContextStore()
    {
        var builder = CreateBuilder(
            new Dictionary<string, string?>
            {
                ["AiGateway:Deployment:Mode"] = "MultiInstance",
                ["AiGateway:FinalAgentContextStore:Provider"] = "Memory"
            });

        var act = () => InvokeAddFinalAgentContextStore(builder);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*FinalAgentContextStore*Redis*");
    }

    [Fact]
    public void AddInfrastructures_ShouldRegisterMemoryContextStoreForSingleInstance()
    {
        var builder = CreateBuilder(
            new Dictionary<string, string?>
            {
                ["AiGateway:Deployment:Mode"] = "SingleInstance",
                ["AiGateway:FinalAgentContextStore:Provider"] = "Memory"
            });

        InvokeAddFinalAgentContextStore(builder);

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<IFinalAgentContextStore>()
            .Should()
            .BeOfType<MemoryCacheFinalAgentContextStore>();
    }

    private static HostApplicationBuilder CreateBuilder(Dictionary<string, string?> overrides)
    {
        var builder = Host.CreateApplicationBuilder();
        var config = new Dictionary<string, string?>
        {
            ["ConnectionStrings:ai-copilot"] = "Host=localhost;Database=aicopilot;Username=postgres;Password=postgres",
            ["Mcp:Runtime:Enabled"] = "false"
        };

        foreach (var item in overrides)
        {
            config[item.Key] = item.Value;
        }

        builder.Configuration.AddInMemoryCollection(config);
        return builder;
    }

    private static void InvokeAddFinalAgentContextStore(HostApplicationBuilder builder)
    {
        var method = typeof(DependencyInjection).GetMethod(
            "AddFinalAgentContextStore",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        try
        {
            method!.Invoke(null, [builder]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
