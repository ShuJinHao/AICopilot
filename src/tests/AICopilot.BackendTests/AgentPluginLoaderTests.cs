using AICopilot.AgentPlugin;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace AICopilot.BackendTests;

public sealed class AgentPluginLoaderTests
{
    [Fact]
    public void UnregisterAgentPlugin_ShouldRemovePluginAndTools()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var loader = new AgentPluginLoader([], provider);
        var plugin = CreatePlugin("runtime-mcp", "Echo");

        loader.RegisterAgentPlugin(plugin);

        loader.GetPlugin("runtime-mcp").Should().NotBeNull();
        loader.GetPluginTools("runtime-mcp").Should().ContainSingle();
        loader.GetAllTools().Should().ContainSingle();

        loader.UnregisterAgentPlugin("runtime-mcp");

        loader.GetPlugin("runtime-mcp").Should().BeNull();
        loader.GetPluginTools("runtime-mcp").Should().BeEmpty();
        loader.GetAllTools().Should().BeEmpty();
    }

    private static GenericBridgePlugin CreatePlugin(string name, string toolName)
    {
        return new GenericBridgePlugin
        {
            Name = name,
            Description = "test plugin",
            ChatExposureMode = ChatExposureMode.Advisory,
            Tools =
            [
                new AiToolDefinition
                {
                    Name = toolName,
                    ToolName = toolName
                }
            ]
        };
    }
}
