using AICopilot.SharedKernel.Ai;
using System.ComponentModel;
using System.Reflection;

namespace AICopilot.AgentPlugin;

public abstract class AgentPluginBase : IAgentPlugin
{
    public virtual string Name { get; }

    public virtual string Description { get; protected set; } = string.Empty;

    protected AgentPluginBase()
    {
        Name = GetType().Name;
    }

    private IEnumerable<MethodInfo> GetToolMethods()
    {
        var type = GetType();
        return type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<DescriptionAttribute>() != null);
    }

    public IEnumerable<AiToolDefinition>? GetTools()
    {
        return GetToolMethods()
            .Select(method => AiToolDefinition.FromMethod(method, this));
    }

    public virtual IEnumerable<string>? HighRiskTools { get; init; }

    public virtual ChatExposureMode ChatExposureMode => ChatExposureMode.Advisory;
}
