namespace AICopilot.AgentPlugin;

public interface IAgentPluginRegistry
{
    void RegisterAgentPlugin(IAgentPlugin plugin);

    void UnregisterAgentPlugin(string name);
}
