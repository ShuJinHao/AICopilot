using AICopilot.AgentPlugin;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.Tools;

public sealed class McpToolRegistryReadService(
    IReadRepository<ToolRegistration> repository,
    IAgentPluginCatalog pluginCatalog)
    : IMcpToolRegistryReadService
{
    public async Task<IReadOnlyCollection<McpToolRegistryReadModel>> GetMcpToolRegistrationsAsync(
        CancellationToken cancellationToken = default)
    {
        var runtimeToolCodes = pluginCatalog
            .GetAllTools()
            .Where(tool => tool.TargetType == AiToolTargetType.McpServer)
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var registrations = await repository.ListAsync(cancellationToken: cancellationToken);
        return registrations
            .Where(tool => tool.ProviderType == ToolProviderType.Mcp &&
                           tool.TargetType == ToolRegistrationTargetType.McpServer)
            .Select(tool => Map(tool, runtimeToolCodes))
            .OrderBy(tool => tool.ServerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tool => tool.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static McpToolRegistryReadModel Map(
        ToolRegistration tool,
        IReadOnlySet<string> runtimeToolCodes)
    {
        var parsedToolName = AiToolIdentity.TryParseRuntimeName(tool.ToolCode, out var identity)
            ? identity!.ToolName
            : tool.ToolCode;

        return new McpToolRegistryReadModel(
            tool.ToolCode,
            tool.TargetName,
            parsedToolName,
            runtimeToolCodes.Contains(tool.ToolCode),
            tool.IsEnabled,
            tool.RiskLevel.ToString(),
            tool.RequiresApproval,
            tool.RequiredPermission,
            tool.UpdatedAt);
    }
}
