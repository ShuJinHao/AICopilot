using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class AgentOutcomeReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AgentOutcomeReconciliationWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private readonly string owner = $"aicopilot-reconciler:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<NodeOutcomeReconciliationCoordinator>();
                var processed = await coordinator.ProcessNextAsync(owner, stoppingToken);
                if (!processed)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Agent outcome reconciliation iteration failed. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                    exception.GetType().Name);
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }
}
