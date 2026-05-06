using AICopilot.SharedKernel.Ai;

namespace AICopilot.AgentPlugin;

public interface IAgentPluginCatalog
{
    AiToolDefinition[] GetTools(params string[] names);

    AiToolDefinition[] GetPluginTools(string name);

    AiToolDefinition[] GetAllTools();

    IAgentPlugin? GetPlugin(string name);

    IAgentPlugin[] GetAllPlugin();
}
