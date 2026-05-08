using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AICopilot.Infrastructure.Mcp;

public sealed class McpServerManager(
    IServiceScopeFactory scopeFactory,
    IOptions<McpRuntimeOptions> options,
    McpRuntimeRegistrySynchronizer synchronizer,
    ILogger<McpServerManager> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var refreshInterval = options.Value.RefreshInterval;
        logger.LogInformation(
            "Starting MCP runtime manager with refresh interval {RefreshIntervalSeconds}s.",
            refreshInterval.TotalSeconds);

        using var timer = new PeriodicTimer(refreshInterval);

        try
        {
            do
            {
                await ReconcileSafelyAsync(synchronizer, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("MCP runtime manager was stopped.");
        }
        finally
        {
            await synchronizer.DisposeAsync();
            logger.LogInformation("Disposed MCP runtime registrations.");
        }
    }

    private async Task ReconcileSafelyAsync(
        McpRuntimeRegistrySynchronizer synchronizer,
        CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var registrationProvider = scope.ServiceProvider.GetRequiredService<IMcpRuntimeRegistrationProvider>();
            await synchronizer.ReconcileAsync(registrationProvider, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MCP runtime reconciliation failed; the previous runtime registry remains active.");
        }
    }
}
