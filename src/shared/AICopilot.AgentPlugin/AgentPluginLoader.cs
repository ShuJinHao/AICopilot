using System.Reflection;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace AICopilot.AgentPlugin;

public class AgentPluginLoader
{
    private readonly IServiceProvider serviceProvider;
    private readonly Dictionary<string, IAgentPlugin> plugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AiToolDefinition[]> tools = new(StringComparer.OrdinalIgnoreCase);

    public AgentPluginLoader(
        IEnumerable<IAgentPluginRegistrar> registrars,
        IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;

        var assemblies = registrars
            .SelectMany(registrar => registrar.Assemblies)
            .Distinct()
            .ToList();

        foreach (var assembly in assemblies)
        {
            LoadPluginsFromAssembly(assembly);
        }
    }

    private void LoadPluginsFromAssembly(Assembly assembly)
    {
        var pluginTypes = assembly.GetTypes()
            .Where(type =>
                typeof(IAgentPlugin).IsAssignableFrom(type) &&
                type is { IsClass: true, IsAbstract: false });

        foreach (var type in pluginTypes)
        {
            var plugin = (IAgentPlugin)ActivatorUtilities.CreateInstance(serviceProvider, type);
            RegisterAgentPlugin(plugin);
        }
    }

    public void RegisterAgentPlugin(IAgentPlugin plugin)
    {
        plugins[plugin.Name] = plugin;
        tools[plugin.Name] = plugin.GetTools()?.ToArray() ?? [];
    }

    public AiToolDefinition[] GetTools(params string[] names)
    {
        var result = new List<AiToolDefinition>();
        foreach (var name in names)
        {
            if (tools.TryGetValue(name, out var pluginTools))
            {
                result.AddRange(pluginTools);
            }
        }

        return result.ToArray();
    }

    public IAgentPlugin? GetPlugin(string name)
    {
        plugins.TryGetValue(name, out var plugin);
        return plugin;
    }

    public IAgentPlugin[] GetAllPlugin()
    {
        return plugins.Values.ToArray();
    }
}
