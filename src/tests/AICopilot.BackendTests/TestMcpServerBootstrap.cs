using System.ComponentModel;
using System.IO.Pipes;
using AICopilot.AgentPlugin;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Infrastructure.Mcp;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AICopilot.BackendTests;

internal sealed class TestMcpServerBootstrap(
    IReadRepository<McpServerInfo> mcpServerRepository,
    IApprovalRequirementReadService approvalRequirementReadService,
    AgentPluginLoader agentPluginLoader,
    ILogger<McpServerBootstrap> logger)
    : McpServerBootstrap(mcpServerRepository, approvalRequirementReadService, agentPluginLoader, logger), IAsyncDisposable
{
    private readonly List<IHost> _serverHosts = [];

    protected override async Task<McpClient> CreateStdioClientAsync(McpServerInfo mcpServerInfo, CancellationToken ct)
    {
        var (serverInput, clientOutput, serverOutput, clientInput) = await CreateConnectedStreamsAsync(ct);
        var serverHost = BuildServerHost(serverInput, serverOutput);

        _serverHosts.Add(serverHost);
        await serverHost.StartAsync(ct);

        var transport = new StreamClientTransport(clientOutput, clientInput, NullLoggerFactory.Instance);
        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

    public async ValueTask DisposeAsync()
    {
        for (var index = _serverHosts.Count - 1; index >= 0; index--)
        {
            _serverHosts[index].Dispose();
        }

        _serverHosts.Clear();
    }

    private static IHost BuildServerHost(Stream serverInput, Stream serverOutput)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        builder.Services
            .AddMcpServer()
            .WithStreamServerTransport(serverInput, serverOutput)
            .WithTools<InProcessTestingMcpTools>();
        builder.Services.AddHostedService<InProcessMcpServerHostedService>();

        return builder.Build();
    }

    private static async Task<(Stream serverInput, Stream clientOutput, Stream serverOutput, Stream clientInput)> CreateConnectedStreamsAsync(
        CancellationToken cancellationToken)
    {
        var inboundPipeName = $"mcp-test-in-{Guid.NewGuid():N}";
        var outboundPipeName = $"mcp-test-out-{Guid.NewGuid():N}";

        var serverInput = new NamedPipeServerStream(
            inboundPipeName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var clientOutput = new NamedPipeClientStream(
            ".",
            inboundPipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous);

        var serverOutput = new NamedPipeServerStream(
            outboundPipeName,
            PipeDirection.Out,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var clientInput = new NamedPipeClientStream(
            ".",
            outboundPipeName,
            PipeDirection.In,
            PipeOptions.Asynchronous);

        await Task.WhenAll(
            serverInput.WaitForConnectionAsync(cancellationToken),
            clientOutput.ConnectAsync(cancellationToken));

        await Task.WhenAll(
            serverOutput.WaitForConnectionAsync(cancellationToken),
            clientInput.ConnectAsync(cancellationToken));

        return (serverInput, clientOutput, serverOutput, clientInput);
    }

    private sealed class InProcessMcpServerHostedService(
        McpServer server,
        IHostApplicationLifetime applicationLifetime) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await server.RunAsync(stoppingToken);
            applicationLifetime.StopApplication();
        }
    }

    private sealed class InProcessTestingMcpTools
    {
        [McpServerTool, Description("Echo the provided input for integration testing.")]
        public static string Echo(string input)
        {
            return $"echo:{input}";
        }
    }
}
