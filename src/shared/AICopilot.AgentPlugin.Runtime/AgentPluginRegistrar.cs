using System.Reflection;

namespace AICopilot.AgentPlugin;

public interface IAgentPluginRegistrar
{
    List<Assembly> Assemblies { get; }

    void RegisterPluginFromAssembly(Assembly assembly);
}

public sealed class AgentPluginRegistrar : IAgentPluginRegistrar
{
    public List<Assembly> Assemblies { get; } = [];

    public void RegisterPluginFromAssembly(Assembly assembly)
    {
        Assemblies.Add(assembly);
    }
}
