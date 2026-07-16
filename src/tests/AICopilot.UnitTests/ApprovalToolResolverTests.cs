using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Plugins;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.SharedKernel.Ai;
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

        var unnamedMcpTool = new AiToolDefinition
        {
            Name = "mcp_runtime_legacy_alias",
            TargetType = AiToolTargetType.McpServer,
            TargetName = "governed-mcp"
        };
        unnamedMcpTool.Identity.Should().BeNull();

        var localPluginTool = new AiToolDefinition
        {
            Name = "local_diagnostic",
            TargetType = AiToolTargetType.Plugin,
            TargetName = "local-plugin"
        };
        localPluginTool.Identity.Should().NotBeNull();
        localPluginTool.Identity!.ToolName.Should().Be(localPluginTool.Name);
    }
}
