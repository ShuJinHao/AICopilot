using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class AgentTaskRunQueueWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AgentTaskRunQueueWorker> logger,
    IOptions<AgentRunQueueOptions>? options = null)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private readonly string leaseOwner = $"aicopilot-dataworker:{Environment.MachineName}";
    private readonly string workerId = $"aicopilot-dataworker:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private readonly string workerVersion = typeof(AgentTaskRunQueueWorker).Assembly.GetName().Version?.ToString() ?? "unknown";
    private AgentRunQueueOptions QueueOptions => options?.Value ?? new AgentRunQueueOptions();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessOnceAsync(stoppingToken);
                if (!processed)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    "Agent task run queue worker iteration failed. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                    ex.GetType().Name);
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    public async Task<bool> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var workerCoordinator = scope.ServiceProvider.GetRequiredService<AgentTaskRunQueueWorkerCoordinator>();

        await MarkHeartbeatAsync(scope.ServiceProvider, null, cancellationToken);
        await workerCoordinator.RecoverExpiredStartedLeasesAsync(cancellationToken);

        var claimed = await workerCoordinator.ClaimNextAsync(
            leaseOwner,
            QueueOptions.LeaseDuration,
            cancellationToken);
        if (!claimed.IsSuccess || claimed.Value is null)
        {
            await MarkHeartbeatAsync(scope.ServiceProvider, null, cancellationToken);
            return false;
        }

        await MarkHeartbeatAsync(scope.ServiceProvider, claimed.Value.QueueItem, cancellationToken);
        try
        {
            await workerCoordinator.ExecuteClaimAsync(claimed.Value, cancellationToken);
        }
        catch (PersistenceCommitOutcomeUnknownException ex)
        {
            logger.LogWarning(
                "Agent task run queue item {QueueItemId} stopped on a commit-unknown boundary. CommitId={CommitId}; the active lease will expire into reconciliation.",
                claimed.Value.QueueItem.Id.Value,
                ex.CommitId);
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Agent task run queue item {QueueItemId} failed before runtime completed. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                claimed.Value.QueueItem.Id.Value,
                ex.GetType().Name);
            await workerCoordinator.FailClaimAsync(
                claimed.Value,
                "agent_task_run_worker_failed",
                "Agent task run worker failed before runtime completed.",
                cancellationToken);
        }
        finally
        {
            await MarkHeartbeatAsync(scope.ServiceProvider, null, cancellationToken);
        }

        return true;
    }

    private async Task MarkHeartbeatAsync(
        IServiceProvider serviceProvider,
        AgentTaskRunQueueItem? activeQueueItem,
        CancellationToken cancellationToken)
    {
        var heartbeatService = serviceProvider.GetService<IAgentWorkerHeartbeatService>();
        if (heartbeatService is null)
        {
            return;
        }

        try
        {
            await heartbeatService.MarkAsync(
                workerId,
                leaseOwner,
                workerVersion,
                activeQueueItem,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                "Agent task run queue worker heartbeat update failed. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                ex.GetType().Name);
        }
    }
}
