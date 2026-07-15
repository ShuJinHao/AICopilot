using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Plugins;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using Microsoft.Extensions.DependencyInjection;

namespace AICopilot.UnitTests;

public sealed class ApprovalToolResolverTests
{
    [Fact]
    public async Task GetToolsForPluginsAsync_ShouldKeepAdvisoryTools()
    {
        var services = new ServiceCollection();
        services.AddAgentPlugin(registrar => registrar.RegisterPluginFromAssembly(typeof(DiagnosticAdvisorPlugin).Assembly));
        using var provider = services.BuildServiceProvider();

        var pluginLoader = provider.GetRequiredService<AgentPluginLoader>();
        var approvalPolicy = new ApprovalPolicy(
            "diagnostic-approval",
            "test",
            ApprovalTargetType.Plugin,
            new DiagnosticAdvisorPlugin().Name,
            [nameof(DiagnosticAdvisorPlugin.GenerateDiagnosticChecklist)],
            true,
            true);

        var queryService = new InMemoryReadRepository<ApprovalPolicy>([approvalPolicy]);

        var requirementResolver = new ApprovalRequirementResolver(queryService);
        var resolver = new ApprovalToolResolver(pluginLoader, requirementResolver);

        var tools = await resolver.GetToolsForPluginsAsync(
            [new DiagnosticAdvisorPlugin().Name]);

        tools.Should().NotBeEmpty();
        tools.Length.Should().Be(new DiagnosticAdvisorPlugin().GetTools()!.Count());
    }
}
