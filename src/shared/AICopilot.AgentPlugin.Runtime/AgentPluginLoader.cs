using System.Reflection;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace AICopilot.AgentPlugin;

public sealed class AgentPluginLoader : IAgentPluginCatalog, IAgentPluginRegistry
{
    private readonly object gate = new();
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
        var pluginTools = (plugin.GetTools() ?? [])
            .Select(tool => EnsureToolIdentity(plugin, tool))
            .ToArray();

        lock (gate)
        {
            EnsureUniqueToolNames(plugin.Name, pluginTools);
            plugins[plugin.Name] = plugin;
            tools[plugin.Name] = pluginTools;
        }
    }

    public void UnregisterAgentPlugin(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        lock (gate)
        {
            plugins.Remove(name);
            tools.Remove(name);
        }
    }

    public AiToolDefinition[] GetTools(params string[] names)
    {
        var result = new List<AiToolDefinition>();

        lock (gate)
        {
            foreach (var name in names)
            {
                if (tools.TryGetValue(name, out var pluginTools))
                {
                    result.AddRange(pluginTools);
                }
            }
        }

        return result.ToArray();
    }

    public AiToolDefinition[] GetPluginTools(string name)
    {
        lock (gate)
        {
            return tools.TryGetValue(name, out var pluginTools)
                ? pluginTools
                : [];
        }
    }

    public AiToolDefinition[] GetAllTools()
    {
        lock (gate)
        {
            return tools.Values.SelectMany(item => item).ToArray();
        }
    }

    public IAgentPlugin? GetPlugin(string name)
    {
        lock (gate)
        {
            plugins.TryGetValue(name, out var plugin);
            return plugin;
        }
    }

    public IAgentPlugin[] GetAllPlugin()
    {
        lock (gate)
        {
            return plugins.Values.ToArray();
        }
    }

    private static AiToolDefinition EnsureToolIdentity(IAgentPlugin plugin, AiToolDefinition tool)
    {
        if (tool.Identity is not null)
        {
            return tool;
        }

        var rawToolName = tool.ToolName ?? tool.Name;
        var risk = plugin.HighRiskTools?.Contains(rawToolName, StringComparer.OrdinalIgnoreCase) == true
            ? AiToolRiskLevel.RequiresApproval
            : AiToolRiskLevel.Low;

        return tool.WithIdentity(
            AiToolTargetType.Plugin,
            plugin.Name,
            rawToolName,
            AiToolExternalSystemType.NonCloud,
            AiToolCapabilityKind.Diagnostics,
            risk);
    }

    private void EnsureUniqueToolNames(string pluginName, AiToolDefinition[] pluginTools)
    {
        var duplicateInPlugin = pluginTools
            .GroupBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateInPlugin is not null)
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginName}' exposes duplicate tool runtime name '{duplicateInPlugin.Key}'.");
        }

        var existingToolNames = tools
            .Where(item => !string.Equals(item.Key, pluginName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(item => item.Value)
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var duplicateGlobal = pluginTools.FirstOrDefault(tool => existingToolNames.Contains(tool.Name));
        if (duplicateGlobal is not null)
        {
            throw new InvalidOperationException(
                $"Tool runtime name '{duplicateGlobal.Name}' is already registered by another plugin.");
        }
    }
}
