using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.SharedKernel.Ai;
using ModelContextProtocol.Client;
using System.Text.Json;

namespace AICopilot.Infrastructure.Mcp;

internal sealed class McpRuntimeToolRegistryProjection(
    McpToolRegistrySynchronizer? toolRegistrySynchronizer)
{
    public async Task SyncAsync(
        McpServerInfo mcpServerInfo,
        IReadOnlyCollection<(McpClientTool Tool, McpAllowedTool Exposure)> exposedTools,
        CancellationToken cancellationToken)
    {
        if (toolRegistrySynchronizer is null || exposedTools.Count == 0)
        {
            return;
        }

        var discoveredTools = exposedTools
            .Select(candidate => new McpDiscoveredToolRegistration(
                AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, mcpServerInfo.Name, candidate.Tool.Name),
                candidate.Tool.Name,
                candidate.Tool.Description,
                JsonSchemaToString(candidate.Tool.JsonSchema),
                JsonSchemaToString(candidate.Tool.ReturnJsonSchema),
                candidate.Exposure.EffectiveRiskLevel(mcpServerInfo.RiskLevel)))
            .ToArray();

        await toolRegistrySynchronizer.UpsertDiscoveredToolsAsync(
            mcpServerInfo.Name,
            discoveredTools,
            cancellationToken);
    }

    private static string JsonSchemaToString(JsonElement? schema)
    {
        return schema.HasValue && schema.Value.ValueKind != JsonValueKind.Undefined
            ? schema.Value.GetRawText()
            : "{}";
    }
}
