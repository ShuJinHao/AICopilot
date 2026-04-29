using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace AICopilot.Infrastructure.Mcp;

public sealed class McpServerManager(
    IServiceScopeFactory scopeFactory,
    ILogger<McpServerManager> logger)
    : BackgroundService
{
    private readonly IList<McpClient> _mcpClients = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("=== MCP Server Manager 启动中 ===");

        try
        {
            using var scope = scopeFactory.CreateScope();
            var bootstrap = scope.ServiceProvider.GetRequiredService<IMcpServerBootstrap>();

            await foreach (var mcpClient in bootstrap.StartAsync(stoppingToken))
            {
                _mcpClients.Add(mcpClient);
            }

            logger.LogInformation("=== MCP Server Manager 启动完成，共托管 {Count} 个服务 ===", _mcpClients.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("MCP Server Manager 启动被取消。");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MCP Server Manager 启动失败，HttpApi 将继续运行。");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("正在关闭 MCP 服务连接...");

        var closeTasks = _mcpClients.Select(async client =>
        {
            try
            {
                await client.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "关闭 MCP 客户端时发生错误。");
            }
        });

        await Task.WhenAll(closeTasks);
        _mcpClients.Clear();

        await base.StopAsync(cancellationToken);
        logger.LogInformation("所有 MCP 服务资源已释放。");
    }
}
