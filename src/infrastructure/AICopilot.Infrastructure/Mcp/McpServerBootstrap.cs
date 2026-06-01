using AICopilot.AgentPlugin;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Core.McpServer.Ids;
using AICopilot.Core.McpServer.Specifications.McpServerInfo;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using System.Runtime.CompilerServices;

namespace AICopilot.Infrastructure.Mcp;

public class McpServerBootstrap(
    IReadRepository<McpServerInfo> mcpServerRepository,
    IApprovalRequirementReadService approvalRequirementReadService,
    IAgentPluginRegistry agentPluginRegistry,
    ILogger<McpServerBootstrap> logger,
    McpToolRegistrySynchronizer? toolRegistrySynchronizer = null)
    : IMcpServerBootstrap, IMcpRuntimeRegistrationProvider
{
    private readonly McpRuntimeProtectedToolReader protectedToolReader = new(approvalRequirementReadService);
    private readonly McpRuntimeToolPluginBuilder toolPluginBuilder = new(logger);
    private readonly McpRuntimeToolRegistryProjection toolRegistryProjection = new(toolRegistrySynchronizer);

    public async IAsyncEnumerable<McpClient> StartAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var candidateServers = await ListCandidateServersAsync(ct);
        foreach (var candidateServer in candidateServers)
        {
            var registration = await CreateRegistrationAsync(candidateServer, ct);
            if (registration is null)
            {
                continue;
            }

            agentPluginRegistry.RegisterAgentPlugin(registration.Plugin);
            if (registration.ClientHandle.Client is McpClient mcpClient)
            {
                yield return mcpClient;
            }
            else
            {
                await registration.DisposeAsync();
            }
        }
    }

    public async Task<IReadOnlyList<McpRuntimeServerState>> ListCandidateServersAsync(
        CancellationToken cancellationToken)
    {
        var mcpServerInfos = await mcpServerRepository.ListAsync(
            new McpServerInfosOrderedSpec(),
            cancellationToken);

        return mcpServerInfos
            .Where(IsRuntimeCandidate)
            .Select(server => new McpRuntimeServerState(server.Id.Value, server.Name, server.RowVersion))
            .ToArray();
    }

    public async Task<McpRuntimeRegistration?> CreateRegistrationAsync(
        McpRuntimeServerState server,
        CancellationToken cancellationToken)
    {
        var mcpServerInfo = await mcpServerRepository.GetByIdAsync(
            new McpServerId(server.ServerId),
            cancellationToken);
        if (mcpServerInfo is null || !IsRuntimeCandidate(mcpServerInfo))
        {
            return null;
        }

        McpClient mcpClient;
        try
        {
            mcpClient = await CreateClientAsync(mcpServerInfo, cancellationToken);
        }
        catch (McpRuntimeStdioCommandUnavailableException ex)
        {
            logger.LogWarning(
                ex,
                "MCP server {Name} was skipped because stdio command {Command} is unavailable: {Reason}",
                mcpServerInfo.Name,
                ex.Command,
                ex.Message);
            return null;
        }

        var clientHandle = new McpRuntimeClientHandle(mcpClient);
        try
        {
            logger.LogInformation("Connected to MCP server {Name}.", mcpServerInfo.Name);

            var tools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);
            var exposedTools = toolPluginBuilder.SelectExposedTools(mcpServerInfo, tools);

            logger.LogInformation(
                "MCP server {Name} discovered {TotalCount} tools and exposed {ExposedCount} after allowlist filtering.",
                mcpServerInfo.Name,
                tools.Count,
                exposedTools.Length);

            if (exposedTools.Length == 0)
            {
                logger.LogWarning(
                    "MCP server {Name} did not match any allowed tool names and will not be registered.",
                    mcpServerInfo.Name);
                await clientHandle.DisposeAsync();
                return null;
            }

            var protectedNames = await protectedToolReader.LoadProtectedToolNamesAsync(mcpServerInfo.Name, cancellationToken);
            await toolRegistryProjection.SyncAsync(mcpServerInfo, exposedTools, cancellationToken);
            var plugin = toolPluginBuilder.BuildMcpPlugin(mcpServerInfo, exposedTools, protectedNames, clientHandle);

            return new McpRuntimeRegistration(
                mcpServerInfo.Id.Value,
                mcpServerInfo.Name,
                mcpServerInfo.RowVersion,
                plugin,
                clientHandle);
        }
        catch
        {
            await clientHandle.DisposeAsync();
            throw;
        }
    }

    private static bool IsRuntimeCandidate(McpServerInfo server)
    {
        return server.IsEnabled
               && server.ChatExposureMode.CanExposeInChat()
               && server.AllowedTools.Count > 0;
    }

    private async Task<McpClient> CreateClientAsync(
        McpServerInfo mcpServerInfo,
        CancellationToken cancellationToken)
    {
        return mcpServerInfo.TransportType switch
        {
            McpTransportType.Stdio => await CreateStdioClientAsync(mcpServerInfo, cancellationToken),
            McpTransportType.Sse => await CreateSseClientAsync(mcpServerInfo, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported MCP transport type: {mcpServerInfo.TransportType}")
        };
    }

    protected virtual async Task<McpClient> CreateStdioClientAsync(McpServerInfo mcpServerInfo, CancellationToken ct)
    {
        return await McpRuntimeClientFactory.CreateStdioClientAsync(mcpServerInfo, logger, ct);
    }

    protected virtual async Task<McpClient> CreateSseClientAsync(McpServerInfo mcpServerInfo, CancellationToken ct)
    {
        return await McpRuntimeClientFactory.CreateSseClientAsync(mcpServerInfo, ct);
    }

    private static string[] ResolveCommandArguments(string rawArguments)
    {
        return McpRuntimeClientFactory.ResolveCommandArguments(rawArguments);
    }
}
