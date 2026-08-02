using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<TestingMcpTools>();

await builder.Build().RunAsync();

public static class TestingMcpServerMarker;

internal sealed class TestingMcpTools
{
    [McpServerTool(
         Name = "queryEcho",
         ReadOnly = true,
         Destructive = false,
         Idempotent = true,
         UseStructuredContent = true),
     Description("Return the provided integration-test input without changing external state.")]
    public static string QueryEcho(string input)
    {
        return $"echo:{input}";
    }

    [McpServerTool(
         Name = "queryEnvironmentSentinel",
         ReadOnly = true,
         Destructive = false,
         Idempotent = true,
         UseStructuredContent = true),
     Description("Report whether the test-only parent environment sentinel was inherited.")]
    public static string QueryEnvironmentSentinel()
    {
        return Environment.GetEnvironmentVariable("AICOPILOT_MCP_SECRET_SENTINEL") is null
            ? "absent"
            : "present";
    }
}
