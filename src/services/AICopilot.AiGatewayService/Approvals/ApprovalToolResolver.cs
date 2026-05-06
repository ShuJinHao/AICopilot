using AICopilot.AgentPlugin;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Approvals;

public class ApprovalToolResolver(
    AgentPluginLoader pluginLoader,
    ApprovalRequirementResolver approvalRequirementResolver)
{
    public async Task<AiToolDefinition[]> GetToolsForPluginsAsync(
        string[] pluginNames,
        CancellationToken cancellationToken = default)
    {
        if (pluginNames.Length == 0)
        {
            return [];
        }

        var requirements = await approvalRequirementResolver.GetRequirementsForTargetsAsync(
            ApprovalTargetType.Plugin,
            pluginNames,
            cancellationToken);

        var tools = new List<AiToolDefinition>();

        foreach (var pluginName in pluginNames)
        {
            var plugin = pluginLoader.GetPlugin(pluginName);
            if (plugin == null || !plugin.ChatExposureMode.CanExposeInChat())
            {
                continue;
            }

            var requirementMap = requirements.GetValueOrDefault(pluginName)
                                 ?? new Dictionary<string, ApprovalRequirement>(StringComparer.OrdinalIgnoreCase);

            tools.AddRange(pluginLoader.GetPluginTools(pluginName).Select(tool => ApplyApprovalRequirement(tool, requirementMap)));
        }

        return tools.ToArray();
    }

    public async Task<AiToolDefinition[]> GetToolsByNamesAsync(
        IReadOnlyCollection<string> toolNames,
        CancellationToken cancellationToken = default)
    {
        if (toolNames.Count == 0)
        {
            return [];
        }

        var normalizedToolNames = new HashSet<string>(
            toolNames.Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);
        if (normalizedToolNames.Count == 0)
        {
            return [];
        }

        var plugins = pluginLoader.GetAllPlugin()
            .Where(plugin => plugin.ChatExposureMode.CanExposeInChat())
            .ToArray();
        var requirements = await approvalRequirementResolver.GetRequirementsForTargetsAsync(
            ApprovalTargetType.Plugin,
            plugins.Select(plugin => plugin.Name).ToArray(),
            cancellationToken);

        var tools = new List<AiToolDefinition>();
        foreach (var plugin in plugins)
        {
            var requirementMap = requirements.GetValueOrDefault(plugin.Name)
                                 ?? new Dictionary<string, ApprovalRequirement>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawTool in pluginLoader.GetPluginTools(plugin.Name))
            {
                if (!normalizedToolNames.Contains(rawTool.Name))
                {
                    continue;
                }

                tools.Add(ApplyApprovalRequirement(rawTool, requirementMap));
            }
        }

        return tools.ToArray();
    }

    private static AiToolDefinition ApplyApprovalRequirement(
        AiToolDefinition tool,
        IReadOnlyDictionary<string, ApprovalRequirement> requirementMap)
    {
        var policyKey = tool.ToolName ?? tool.Name;
        return requirementMap.TryGetValue(policyKey, out var requirement) && requirement.RequiresApproval
            ? tool.WithRequiresApproval(true)
            : tool;
    }
}
