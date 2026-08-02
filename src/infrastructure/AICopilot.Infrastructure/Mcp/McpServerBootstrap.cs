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
    IMcpToolRegistryReadService toolRegistryReadService,
    IAgentPluginRegistry agentPluginRegistry,
    ILogger<McpServerBootstrap> logger,
    McpToolRegistrySynchronizer? toolRegistrySynchronizer = null)
    : IMcpServerBootstrap, IMcpRuntimeRegistrationProvider
{
    private readonly McpRuntimeToolGovernanceReader governanceReader = new(toolRegistryReadService);
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
        using var discoveryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        discoveryCts.CancelAfter(DiscoveryDeadline);
        var registrationTask = CreateRegistrationCoreAsync(server, discoveryCts.Token);

        try
        {
            return await registrationTask.WaitAsync(DiscoveryDeadline, cancellationToken);
        }
        catch (TimeoutException)
        {
            discoveryCts.Cancel();
            ObserveLateRegistration(registrationTask, server.Name);
            await MarkUnavailableAsync(server.Name, cancellationToken);
            logger.LogWarning(
                "MCP server discovery exceeded the independent {DiscoveryDeadlineSeconds}s deadline and was quarantined.",
                DiscoveryDeadline.TotalSeconds);
            return null;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            discoveryCts.IsCancellationRequested)
        {
            await MarkUnavailableAsync(server.Name, cancellationToken);
            logger.LogWarning(
                "MCP server discovery exceeded the independent {DiscoveryDeadlineSeconds}s deadline and was quarantined.",
                DiscoveryDeadline.TotalSeconds);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            discoveryCts.Cancel();
            if (!registrationTask.IsCompleted)
            {
                ObserveLateRegistration(registrationTask, server.Name);
            }

            throw;
        }
    }

    protected virtual TimeSpan DiscoveryDeadline =>
        TimeSpan.FromSeconds(McpRuntimeOptions.DiscoveryDeadlineSeconds);

    private async Task<McpRuntimeRegistration?> CreateRegistrationCoreAsync(
        McpRuntimeServerState server,
        CancellationToken cancellationToken)
    {
        var mcpServerInfo = await mcpServerRepository.GetByIdAsync(
            new McpServerId(server.ServerId),
            cancellationToken);
        if (mcpServerInfo is null || !IsRuntimeCandidate(mcpServerInfo))
        {
            await MarkUnavailableAsync(server.Name, cancellationToken);
            return null;
        }

        McpClient mcpClient;
        try
        {
            mcpClient = await CreateClientAsync(mcpServerInfo, cancellationToken);
        }
        catch (McpRuntimeStdioCommandUnavailableException ex)
        {
            await MarkUnavailableAsync(mcpServerInfo.Name, cancellationToken);
            logger.LogWarning(
                "MCP server {Name} was skipped because stdio command {Command} is unavailable. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                mcpServerInfo.Name,
                ex.Command,
                ex.GetType().Name);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await MarkUnavailableAsync(mcpServerInfo.Name, cancellationToken);
            throw;
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

            await toolRegistryProjection.SyncAsync(mcpServerInfo, exposedTools, cancellationToken);

            if (exposedTools.Length == 0)
            {
                logger.LogWarning(
                    "MCP server {Name} did not match any allowed tool names and will not be registered.",
                    mcpServerInfo.Name);
                await clientHandle.DisposeAsync();
                return null;
            }

            var governance = await governanceReader.LoadAsync(mcpServerInfo.Name, cancellationToken);
            var toolBindings = toolPluginBuilder.BindGovernance(
                mcpServerInfo.Name,
                exposedTools,
                governance);
            var plugin = toolPluginBuilder.BuildMcpPlugin(mcpServerInfo, toolBindings, clientHandle);
            var schemaFingerprint = McpRuntimeToolContract.ComputeFingerprint(
                mcpServerInfo.Name,
                toolBindings);

            return new McpRuntimeRegistration(
                mcpServerInfo.Id.Value,
                mcpServerInfo.Name,
                mcpServerInfo.RowVersion,
                schemaFingerprint,
                plugin,
                clientHandle);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await clientHandle.DisposeAsync();
            throw;
        }
        catch
        {
            await clientHandle.DisposeAsync();
            await MarkUnavailableAsync(mcpServerInfo.Name, cancellationToken);
            throw;
        }
    }

    public Task QuarantineServerAsync(
        McpRuntimeServerState server,
        CancellationToken cancellationToken)
    {
        return MarkUnavailableAsync(server.Name, cancellationToken);
    }

    private void ObserveLateRegistration(
        Task<McpRuntimeRegistration?> registrationTask,
        string serverName)
    {
        _ = ObserveLateRegistrationCoreAsync(registrationTask, serverName);
    }

    private async Task ObserveLateRegistrationCoreAsync(
        Task<McpRuntimeRegistration?> registrationTask,
        string serverName)
    {
        try
        {
            var lateRegistration = await registrationTask.ConfigureAwait(false);
            if (lateRegistration is not null)
            {
                await lateRegistration.DisposeAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // The server-specific deadline or caller cancellation was observed.
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Late MCP bootstrap discovery completion was observed after quarantine. Server={Name}; ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                serverName,
                ex.GetType().Name);
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

    private async Task MarkUnavailableAsync(
        string serverName,
        CancellationToken cancellationToken)
    {
        try
        {
            await toolRegistryProjection.MarkUnavailableAsync(serverName, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Failed to quarantine MCP registrations for {Name}. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                serverName,
                ex.GetType().Name);
        }
    }
}
