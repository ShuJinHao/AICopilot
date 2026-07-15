using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();

builder.Services
    .AddMcpServer()
    .WithStreamServerTransport(Console.OpenStandardInput(), Console.OpenStandardOutput())
    .WithTools<TestingMcpTools>();
builder.Services.AddHostedService<McpServerHostedService>();

await builder.Build().RunAsync();

public static class TestingMcpServerMarker;

internal sealed class TestingMcpTools
{
    [McpServerTool, Description("Echo the provided input for integration testing.")]
    public static string Echo(string input)
    {
        return $"echo:{input}";
    }
}

internal sealed class McpServerHostedService(
    McpServer server,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await server.RunAsync(stoppingToken);
        applicationLifetime.StopApplication();
    }
}
