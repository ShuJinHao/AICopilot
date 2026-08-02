using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.Infrastructure.Mcp;

internal sealed class McpRuntimeToolRegistryProjection(
    McpToolRegistrySynchronizer? toolRegistrySynchronizer)
{
    public async Task SyncAsync(
        McpServerInfo mcpServerInfo,
        IReadOnlyCollection<McpRuntimeToolCandidate> exposedTools,
        CancellationToken cancellationToken)
    {
        if (toolRegistrySynchronizer is null)
        {
            return;
        }

        var discoveredTools = exposedTools
            .Select(candidate => new McpDiscoveredToolRegistration(
                AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, mcpServerInfo.Name, candidate.Tool.Name),
                candidate.Tool.Name,
                candidate.Tool.Description,
                candidate.InputSchema.GetRawText(),
                candidate.OutputSchema.GetRawText(),
                candidate.RiskLevel))
            .ToArray();

        await toolRegistrySynchronizer.UpsertDiscoveredToolsAsync(
            mcpServerInfo.Name,
            discoveredTools,
            cancellationToken);
    }

    public async Task MarkUnavailableAsync(
        string serverName,
        CancellationToken cancellationToken)
    {
        if (toolRegistrySynchronizer is null)
        {
            return;
        }

        await toolRegistrySynchronizer.UpsertDiscoveredToolsAsync(
            serverName,
            [],
            cancellationToken);
    }
}
