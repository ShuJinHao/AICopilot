using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Approvals;

public class ApprovalToolResolver(
    IAgentPluginCatalog pluginCatalog,
    ApprovalRequirementResolver approvalRequirementResolver,
    ToolRegistryGuard? toolRegistryGuard = null,
    ICurrentUser? currentUser = null)
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
            var plugin = pluginCatalog.GetPlugin(pluginName);
            if (plugin == null || !plugin.ChatExposureMode.CanExposeInChat())
            {
                continue;
            }

            var requirementMap = requirements.GetValueOrDefault(pluginName)
                                 ?? new Dictionary<string, ApprovalRequirement>(StringComparer.OrdinalIgnoreCase);

            var exposedTools = await FilterRegistryControlledToolsAsync(
                pluginCatalog.GetPluginTools(pluginName),
                cancellationToken);
            tools.AddRange(exposedTools.Select(tool => ApplyApprovalRequirement(tool, requirementMap)));
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

        var plugins = pluginCatalog.GetAllPlugin()
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

            foreach (var rawTool in pluginCatalog.GetPluginTools(plugin.Name))
            {
                if (!normalizedToolNames.Contains(rawTool.Name))
                {
                    continue;
                }

                var filtered = await FilterRegistryControlledToolsAsync([rawTool], cancellationToken);
                tools.AddRange(filtered.Select(tool => ApplyApprovalRequirement(tool, requirementMap)));
            }
        }

        return tools.ToArray();
    }

    private async Task<IReadOnlyCollection<AiToolDefinition>> FilterRegistryControlledToolsAsync(
        IReadOnlyCollection<AiToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        if (tools.Count == 0)
        {
            return [];
        }

        var userId = currentUser?.Id;
        var result = new List<AiToolDefinition>();
        foreach (var tool in tools)
        {
            if (tool.TargetType != AiToolTargetType.McpServer)
            {
                result.Add(tool);
                continue;
            }

            if (toolRegistryGuard is null || userId is null)
            {
                continue;
            }

            var decision = await toolRegistryGuard.ValidateAsync(tool.Name, userId.Value, cancellationToken);
            if (decision.IsAllowed && decision.Tool is not null)
            {
                result.Add(tool.WithRequiresApproval(tool.RequiresApproval || decision.Tool.RequiresApproval));
            }
        }

        return result;
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
