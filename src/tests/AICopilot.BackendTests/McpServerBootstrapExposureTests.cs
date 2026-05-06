using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Plugins;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Infrastructure.Mcp;
using AICopilot.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace AICopilot.BackendTests;

[Trait("Suite", "Phase43SafetyQuality")]
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
    public async Task StartAsync_ShouldOnlyRegisterEnabledAllowlistedAdvisoryServers()
    {
        var exposedServer = new McpServerInfo(
            "advisory-mcp",
            "advisory server",
            McpTransportType.Stdio,
            "dotnet",
            string.Empty,
            ChatExposureMode.Advisory,
            [new McpAllowedTool("Echo")],
            true);

        var controlServer = new McpServerInfo(
            "control-mcp",
            "control server",
            McpTransportType.Stdio,
            "dotnet",
            string.Empty,
            ChatExposureMode.Control,
            [new McpAllowedTool("Echo")],
            true);

        var closedServer = new McpServerInfo(
            "closed-mcp",
            "closed server",
            McpTransportType.Stdio,
            "dotnet",
            string.Empty,
            ChatExposureMode.Advisory,
            [],
            true);

        var serverRepository = new InMemoryReadRepository<McpServerInfo>([exposedServer, controlServer, closedServer]);
        var approvalPolicyRepository = new InMemoryReadRepository<ApprovalPolicy>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentPlugin(registrar => registrar.RegisterPluginFromAssembly(typeof(DiagnosticAdvisorPlugin).Assembly));
        using var provider = services.BuildServiceProvider();

        var loader = provider.GetRequiredService<AgentPluginLoader>();
        await using var bootstrap = new TestMcpServerBootstrap(
            serverRepository,
            approvalPolicyRepository,
            loader,
            NullLogger<McpServerBootstrap>.Instance);

        var clients = new List<IAsyncDisposable>();
        await foreach (var client in bootstrap.StartAsync(CancellationToken.None))
        {
            clients.Add(client);
        }

        try
        {
            clients.Should().HaveCount(1);
            loader.GetPlugin("advisory-mcp").Should().NotBeNull();
            loader.GetPlugin("control-mcp").Should().BeNull();
            loader.GetPlugin("closed-mcp").Should().BeNull();
            loader.GetPlugin("advisory-mcp")!.GetTools()!.Should().HaveCount(1);
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }

    private static string[] ResolveCommandArguments(string rawArguments)
    {
        var method = typeof(McpServerBootstrap).GetMethod(
            "ResolveCommandArguments",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        return (string[])method!.Invoke(null, [rawArguments])!;
    }
}
