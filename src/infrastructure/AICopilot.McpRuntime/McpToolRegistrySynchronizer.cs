using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.Infrastructure.Mcp;

public sealed record McpDiscoveredToolRegistration(
    string ToolCode,
    string ToolName,
    string? Description,
    string InputSchemaJson,
    string OutputSchemaJson,
    AiToolRiskLevel RiskLevel);

public sealed class McpToolRegistrySynchronizer(IRepository<ToolRegistration> toolRepository)
{
    public async Task UpsertDiscoveredToolsAsync(
        string serverName,
        IReadOnlyCollection<McpDiscoveredToolRegistration> tools,
        CancellationToken cancellationToken)
    {
        if (tools.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var discoveredTool in tools)
        {
            var existing = await toolRepository.GetAsync(
                item => item.ToolCode == discoveredTool.ToolCode,
                cancellationToken: cancellationToken);

            if (existing is null)
            {
                toolRepository.Add(new ToolRegistration(
                    discoveredTool.ToolCode,
                    BuildDisplayName(serverName, discoveredTool.ToolName),
                    BuildDescription(serverName, discoveredTool),
                    ToolProviderType.Mcp,
                    ToolRegistrationTargetType.McpServer,
                    serverName,
                    discoveredTool.InputSchemaJson,
                    discoveredTool.OutputSchemaJson,
                    discoveredTool.RiskLevel,
                    requiredPermission: null,
                    requiresApproval: true,
                    isEnabled: false,
                    timeoutSeconds: 120,
                    ToolAuditLevel.Standard,
                    now));
                continue;
            }

            existing.Update(
                BuildDisplayName(serverName, discoveredTool.ToolName),
                BuildDescription(serverName, discoveredTool),
                ToolProviderType.Mcp,
                ToolRegistrationTargetType.McpServer,
                serverName,
                discoveredTool.InputSchemaJson,
                discoveredTool.OutputSchemaJson,
                existing.RiskLevel,
                existing.RequiredPermission,
                existing.RequiresApproval,
                existing.IsEnabled,
                existing.TimeoutSeconds,
                existing.AuditLevel,
                now);
            toolRepository.Update(existing);
        }

        await toolRepository.SaveChangesAsync(cancellationToken);
    }

    private static string BuildDisplayName(string serverName, string toolName)
    {
        var displayName = $"{serverName}:{toolName}";
        return displayName.Length <= 160 ? displayName : displayName[..160];
    }

    private static string BuildDescription(string serverName, McpDiscoveredToolRegistration tool)
    {
        var description = string.IsNullOrWhiteSpace(tool.Description)
            ? $"MCP tool discovered from server '{serverName}'."
            : tool.Description.Trim();
        return description.Length <= 1000 ? description : description[..1000];
    }
}
