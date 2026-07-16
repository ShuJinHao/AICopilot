using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Plugins;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Infrastructure.Mcp;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using System.Reflection;

namespace AICopilot.ToolPlugin.ConformanceTests;

public sealed class McpServerBootstrapExposureTests
{
    [Fact]
    public void ResolveCommandArguments_ShouldHonorQuotesEscapesAndSingleFilePaths()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "aicopilot-mcp-args", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var tempFile = Path.Combine(tempDirectory, "server with spaces.dll");
        File.WriteAllText(tempFile, string.Empty);

        try
        {
            ResolveCommandArguments(tempFile).Should().Equal(tempFile);

            ResolveCommandArguments("\"C:\\Program Files\\MCP Server\\server.dll\" --flag \"two words\" --name \"hello \\\"world\\\"\" plain\\ value")
                .Should().Equal(
                    "C:\\Program Files\\MCP Server\\server.dll",
                    "--flag",
                    "two words",
                    "--name",
                    "hello \"world\"",
                    "plain value");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateRegistrationAsync_ShouldRejectUnsafePersistedSseEndpointThroughTheRealFactoryPath()
    {
        var server = new McpServerInfo(
            "sse-mcp",
            "SSE server",
            McpTransportType.Sse,
            string.Empty,
            "https://mcp.example.test/events",
            externalSystemType: AiToolExternalSystemType.CloudReadOnly,
            capabilityKind: AiToolCapabilityKind.ReadOnlyQuery,
            chatExposureMode: ChatExposureMode.Advisory,
            allowedTools: [new McpAllowedTool("QueryDeviceLogs", ReadOnlyDeclared: true, McpReadOnlyHint: true, McpDestructiveHint: false)],
            isEnabled: true);
        typeof(McpServerInfo)
            .GetField("<Arguments>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(server, "http://127.0.0.1/events");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentPlugin(registrar => registrar.RegisterPluginFromAssembly(typeof(DiagnosticAdvisorPlugin).Assembly));
        using var provider = services.BuildServiceProvider();

        var bootstrap = new McpServerBootstrap(
            new InMemoryReadRepository<McpServerInfo>([server]),
            new TestApprovalRequirementReadService(),
            provider.GetRequiredService<AgentPluginLoader>(),
            NullLogger<McpServerBootstrap>.Instance);

        var action = () => bootstrap.CreateRegistrationAsync(
            new McpRuntimeServerState(server.Id.Value, server.Name, server.RowVersion),
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("MCP SSE server sse-mcp endpoint is invalid: *loopback*");
    }

    [Fact]
    public async Task StartAsync_ShouldOnlyRegisterEnabledAllowlistedAdvisoryServers()
    {
        var exposedServer = new McpServerInfo(
            "advisory-mcp",
            "advisory server",
            McpTransportType.Stdio,
            "dotnet",
            string.Empty,
            externalSystemType: AiToolExternalSystemType.CloudReadOnly,
            capabilityKind: AiToolCapabilityKind.ReadOnlyQuery,
            chatExposureMode: ChatExposureMode.Advisory,
            allowedTools: [new McpAllowedTool("QueryDeviceLogs", ReadOnlyDeclared: true, McpReadOnlyHint: true, McpDestructiveHint: false)],
            isEnabled: true);

        var controlServer = new McpServerInfo(
            "control-mcp",
            "control server",
            McpTransportType.Stdio,
            "dotnet",
            string.Empty,
            externalSystemType: AiToolExternalSystemType.CloudReadOnly,
            capabilityKind: AiToolCapabilityKind.ReadOnlyQuery,
            chatExposureMode: ChatExposureMode.Control,
            allowedTools: [new McpAllowedTool("QueryDeviceLogs", ReadOnlyDeclared: true, McpReadOnlyHint: true, McpDestructiveHint: false)],
            isEnabled: true);

        var closedServer = new McpServerInfo(
            "closed-mcp",
            "closed server",
            McpTransportType.Stdio,
            "dotnet",
            string.Empty,
            externalSystemType: AiToolExternalSystemType.CloudReadOnly,
            capabilityKind: AiToolCapabilityKind.ReadOnlyQuery,
            chatExposureMode: ChatExposureMode.Advisory,
            allowedTools: [],
            isEnabled: true);

        var destructiveCloudServer = new McpServerInfo(
            "destructive-cloud",
            "Cloud server with a destructive runtime hint",
            McpTransportType.Stdio,
            "dotnet",
            string.Empty,
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.ReadOnlyQuery,
            ChatExposureMode.Advisory,
            [
                new McpAllowedTool(
                    "QueryDeviceLogs",
                    ReadOnlyDeclared: true,
                    McpReadOnlyHint: true,
                    McpDestructiveHint: false)
            ],
            true,
            AiToolRiskLevel.Low);
        ReplaceAllowedTools(
            destructiveCloudServer,
            [
                new McpAllowedTool(
                    "QueryDeviceLogs",
                    ReadOnlyDeclared: true,
                    McpReadOnlyHint: true,
                    McpDestructiveHint: true)
            ]);

        var opaqueRelabeledWriteServer = new McpServerInfo(
            "gateway-a17",
            "Opaque remote runtime at https://relay.example.test/mcp",
            McpTransportType.Stdio,
            "dotnet",
            "https://relay.example.test/mcp",
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.ReadOnlyQuery,
            ChatExposureMode.Advisory,
            [new McpAllowedTool("QueryDeviceLogs", ReadOnlyDeclared: true, McpReadOnlyHint: true)],
            true,
            AiToolRiskLevel.Low);
        typeof(McpServerInfo)
            .GetField("<ExternalSystemType>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(opaqueRelabeledWriteServer, AiToolExternalSystemType.NonCloud);
        typeof(McpServerInfo)
            .GetField("<CapabilityKind>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(opaqueRelabeledWriteServer, AiToolCapabilityKind.SideEffecting);
        ReplaceAllowedTools(
            opaqueRelabeledWriteServer,
            [new McpAllowedTool("deleteDevice", ReadOnlyDeclared: true, McpReadOnlyHint: true)]);

        var unknownServer = new McpServerInfo(
            "unknown-runtime",
            "Unknown target metadata must fail closed",
            McpTransportType.Stdio,
            "dotnet",
            string.Empty,
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.ReadOnlyQuery,
            ChatExposureMode.Advisory,
            [new McpAllowedTool("QueryDeviceLogs", ReadOnlyDeclared: true, McpReadOnlyHint: true)],
            true,
            AiToolRiskLevel.Low);
        typeof(McpServerInfo)
            .GetField("<ExternalSystemType>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(unknownServer, AiToolExternalSystemType.Unknown);

        var serverRepository = new InMemoryReadRepository<McpServerInfo>(
            [
                exposedServer,
                controlServer,
                closedServer,
                destructiveCloudServer,
                opaqueRelabeledWriteServer,
                unknownServer
            ]);
        var approvalRequirementReadService = new TestApprovalRequirementReadService();
        var toolRepository = new ToolRegistryGovernanceTestBase.InMemoryRepository<ToolRegistration>();
        var registrySynchronizer = new McpToolRegistrySynchronizer(toolRepository);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentPlugin(registrar => registrar.RegisterPluginFromAssembly(typeof(DiagnosticAdvisorPlugin).Assembly));
        using var provider = services.BuildServiceProvider();

        var loader = provider.GetRequiredService<AgentPluginLoader>();
        var bootstrapLogger = new RecordingBootstrapLogger();
        await using var bootstrap = new TestMcpServerBootstrap(
            serverRepository,
            approvalRequirementReadService,
            loader,
            bootstrapLogger,
            registrySynchronizer);

        var clients = new List<IAsyncDisposable>();
        await foreach (var client in bootstrap.StartAsync(CancellationToken.None))
        {
            clients.Add(client);
        }

        try
        {
            clients.Should().HaveCount(1, string.Join(Environment.NewLine, bootstrapLogger.Messages));
            loader.GetPlugin("advisory-mcp").Should().NotBeNull();
            foreach (var excludedPlugin in new[]
                     {
                         "control-mcp",
                         "closed-mcp",
                         "destructive-cloud",
                         "gateway-a17",
                         "unknown-runtime"
                     })
            {
                loader.GetPlugin(excludedPlugin).Should().BeNull();
            }
            loader.GetPlugin("advisory-mcp")!.GetTools()!.Should().HaveCount(1);
            toolRepository.Items.Should().ContainSingle(registration =>
                registration.TargetName == "advisory-mcp" &&
                registration.ToolCode == AiToolIdentity.CreateRuntimeName(
                    AiToolTargetType.McpServer,
                    "advisory-mcp",
                    "QueryDeviceLogs"));
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task StartAsync_ShouldSkipEnabledStdioServer_WhenCommandIsMissing()
    {
        var missingCommand = $"aicopilot-missing-mcp-command-{Guid.NewGuid():N}";
        var server = new McpServerInfo(
            "missing-command-mcp",
            "server with missing command",
            McpTransportType.Stdio,
            missingCommand,
            string.Empty,
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.ReadOnlyQuery,
            ChatExposureMode.Advisory,
            [new McpAllowedTool("QueryDeviceLogs", ReadOnlyDeclared: true, McpReadOnlyHint: true, McpDestructiveHint: false)],
            true);

        var serverRepository = new InMemoryReadRepository<McpServerInfo>([server]);
        var approvalRequirementReadService = new TestApprovalRequirementReadService();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentPlugin(registrar => registrar.RegisterPluginFromAssembly(typeof(DiagnosticAdvisorPlugin).Assembly));
        using var provider = services.BuildServiceProvider();

        var loader = provider.GetRequiredService<AgentPluginLoader>();
        var bootstrap = new McpServerBootstrap(
            serverRepository,
            approvalRequirementReadService,
            loader,
            NullLogger<McpServerBootstrap>.Instance);

        var clients = new List<IAsyncDisposable>();
        await foreach (var client in bootstrap.StartAsync(CancellationToken.None))
        {
            clients.Add(client);
        }

        clients.Should().BeEmpty();
        loader.GetPlugin("missing-command-mcp").Should().BeNull();
    }

    [Fact]
    public async Task CreateRegistrationAsync_ShouldNotSwallowNonCommandMissingErrors()
    {
        var server = new McpServerInfo(
            "throwing-mcp",
            "server with runtime failure",
            McpTransportType.Stdio,
            "dotnet",
            string.Empty,
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.ReadOnlyQuery,
            ChatExposureMode.Advisory,
            [new McpAllowedTool("QueryDeviceLogs", ReadOnlyDeclared: true, McpReadOnlyHint: true, McpDestructiveHint: false)],
            true);

        var serverRepository = new InMemoryReadRepository<McpServerInfo>([server]);
        var approvalRequirementReadService = new TestApprovalRequirementReadService();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentPlugin(registrar => registrar.RegisterPluginFromAssembly(typeof(DiagnosticAdvisorPlugin).Assembly));
        using var provider = services.BuildServiceProvider();

        var bootstrap = new ThrowingMcpServerBootstrap(
            serverRepository,
            approvalRequirementReadService,
            provider.GetRequiredService<AgentPluginLoader>());

        var action = () => bootstrap.CreateRegistrationAsync(
            new McpRuntimeServerState(server.Id.Value, server.Name, server.RowVersion),
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("synthetic MCP runtime failure");
    }

    private static string[] ResolveCommandArguments(string rawArguments)
    {
        var method = typeof(McpServerBootstrap).GetMethod(
            "ResolveCommandArguments",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        return (string[])method!.Invoke(null, [rawArguments])!;
    }

    private static void ReplaceAllowedTools(McpServerInfo server, IEnumerable<McpAllowedTool> tools)
    {
        var allowedTools = (List<McpAllowedTool>)typeof(McpServerInfo)
            .GetField("_allowedTools", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(server)!;
        allowedTools.Clear();
        allowedTools.AddRange(tools);
    }

    private sealed class ThrowingMcpServerBootstrap(
        IReadRepository<McpServerInfo> mcpServerRepository,
        IApprovalRequirementReadService approvalRequirementReadService,
        IAgentPluginRegistry agentPluginRegistry)
        : McpServerBootstrap(
            mcpServerRepository,
            approvalRequirementReadService,
            agentPluginRegistry,
            NullLogger<McpServerBootstrap>.Instance)
    {
        protected override Task<McpClient> CreateStdioClientAsync(McpServerInfo mcpServerInfo, CancellationToken ct)
        {
            throw new InvalidOperationException("synthetic MCP runtime failure");
        }
    }

    private sealed class RecordingBootstrapLogger : ILogger<McpServerBootstrap>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
