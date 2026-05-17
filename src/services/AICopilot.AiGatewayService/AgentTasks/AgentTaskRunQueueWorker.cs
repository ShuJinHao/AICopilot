using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.AiGatewayService.Tools;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class AgentTaskRunQueueWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AgentTaskRunQueueWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private readonly string leaseOwner = $"aicopilot-dataworker:{Environment.MachineName}";
    private readonly string workerId = $"aicopilot-dataworker:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private readonly string workerVersion = typeof(AgentTaskRunQueueWorker).Assembly.GetName().Version?.ToString() ?? "unknown";

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
                logger.LogError(ex, "Agent task run queue worker iteration failed.");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    public async Task<bool> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var queueRepository = scope.ServiceProvider.GetRequiredService<IRepository<AgentTaskRunQueueItem>>();
        var taskRepository = scope.ServiceProvider.GetRequiredService<IRepository<AgentTask>>();
        var attemptRepository = scope.ServiceProvider.GetRequiredService<IRepository<AgentTaskRunAttempt>>();
        var runQueue = scope.ServiceProvider.GetRequiredService<IAgentTaskRunQueue>();

        await MarkHeartbeatAsync(scope.ServiceProvider, null, cancellationToken);
        await FailExpiredStartedLeasesAsync(queueRepository, taskRepository, attemptRepository, cancellationToken);

        var leased = await runQueue.LeaseNextAsync(leaseOwner, LeaseDuration, cancellationToken);
        if (!leased.IsSuccess || leased.Value is null)
        {
            await MarkHeartbeatAsync(scope.ServiceProvider, null, cancellationToken);
            return false;
        }

        await MarkHeartbeatAsync(scope.ServiceProvider, leased.Value, cancellationToken);
        try
        {
            var runtime = scope.ServiceProvider.GetRequiredService<IAgentTaskRuntime>();
            await ExecuteQueueItemAsync(
                leased.Value,
                queueRepository,
                taskRepository,
                attemptRepository,
                runtime,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent task run queue item {QueueItemId} failed before runtime completed.", leased.Value.Id.Value);
            await FailQueueItemAsync(
                leased.Value,
                queueRepository,
                taskRepository,
                attemptRepository,
                "agent_task_run_worker_failed",
                ex.Message,
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
            logger.LogWarning(ex, "Agent task run queue worker heartbeat update failed.");
        }
    }

    private static async Task FailExpiredStartedLeasesAsync(
        IRepository<AgentTaskRunQueueItem> queueRepository,
        IRepository<AgentTask> taskRepository,
        IRepository<AgentTaskRunAttempt> attemptRepository,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var activeItems = await queueRepository.ListAsync(
            new AgentTaskRunQueueActiveItemsSpec(),
            cancellationToken);
        foreach (var item in activeItems.Where(candidate => candidate.IsExpiredStartedLease(now)))
        {
            var message = "Agent task run queue lease expired during execution. Retry the task before continuing.";
            item.MarkFailed(AppProblemCodes.AgentTaskRunQueueLeaseExpired, message, now);
            queueRepository.Update(item);

            var task = await taskRepository.FirstOrDefaultAsync(
                new AgentTaskByIdSpec(item.TaskId, includeSteps: true),
                cancellationToken);
            if (task is not null)
            {
                task.Fail(message, now);
                task.ReleaseRunLease(now, clearActiveAttempt: true);
                taskRepository.Update(task);
            }

            var attempt = await ResolveAttemptAsync(item, task, attemptRepository, cancellationToken);
            if (attempt is not null && !attempt.IsTerminal)
            {
                attempt.MarkFailed(AppProblemCodes.AgentTaskRunLeaseExpired, message, now);
                attemptRepository.Update(attempt);
            }
        }

        await queueRepository.SaveChangesAsync(cancellationToken);
    }

    private static async Task ExecuteQueueItemAsync(
        AgentTaskRunQueueItem item,
        IRepository<AgentTaskRunQueueItem> queueRepository,
        IRepository<AgentTask> taskRepository,
        IRepository<AgentTaskRunAttempt> attemptRepository,
        IAgentTaskRuntime runtime,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdSpec(item.TaskId, includeSteps: true),
            cancellationToken);
        if (task is null)
        {
            item.MarkFailed(
                AppProblemCodes.AgentTaskRunQueueNotFound,
                "Agent task was not found for queued run.",
                now);
            queueRepository.Update(item);
            await queueRepository.SaveChangesAsync(cancellationToken);
            return;
        }

        item.MarkStarted(task.ActiveRunAttemptId, now);
        queueRepository.Update(item);
        await queueRepository.SaveChangesAsync(cancellationToken);

        var result = await runtime.RunAsync(task, item.TriggerType, cancellationToken);
        now = DateTimeOffset.UtcNow;

        var latestAttempt = await ResolveAttemptAsync(item, task, attemptRepository, cancellationToken);
        if (latestAttempt is not null)
        {
            item.LinkRunAttempt(latestAttempt.Id, now);
        }

        if (!result.IsSuccess)
        {
            var problem = result.Errors?.OfType<ApiProblemDescriptor>().FirstOrDefault();
            item.MarkFailed(
                problem?.Code ?? "agent_task_run_failed",
                problem?.Detail ?? "Agent task run failed before runtime execution completed.",
                now);
        }
        else if (task.Status == AgentTaskStatus.Cancelled)
        {
            item.Cancel(now, "Agent task cancellation requested.");
        }
        else if (task.Status == AgentTaskStatus.Failed)
        {
            item.MarkFailed(
                latestAttempt?.FailureCode ?? "agent_task_failed",
                latestAttempt?.SafeMessage ?? task.FinalSummary ?? "Agent task failed.",
                now);
        }
        else
        {
            item.MarkSucceeded(now, $"Agent task run reached {task.Status}.");
        }

        queueRepository.Update(item);
        await queueRepository.SaveChangesAsync(cancellationToken);
    }

    private static async Task FailQueueItemAsync(
        AgentTaskRunQueueItem item,
        IRepository<AgentTaskRunQueueItem> queueRepository,
        IRepository<AgentTask> taskRepository,
        IRepository<AgentTaskRunAttempt> attemptRepository,
        string failureCode,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var sanitized = ToolExecutionRecordSanitizer.Sanitize(safeMessage, 2000) ?? "Agent task run worker failed.";
        item.MarkFailed(failureCode, sanitized, now);
        queueRepository.Update(item);

        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdSpec(item.TaskId, includeSteps: true),
            cancellationToken);
        if (task is not null)
        {
            task.Fail(sanitized, now);
            task.ReleaseRunLease(now, clearActiveAttempt: true);
            taskRepository.Update(task);
        }

        var attempt = await ResolveAttemptAsync(item, task, attemptRepository, cancellationToken);
        if (attempt is not null && !attempt.IsTerminal)
        {
            attempt.MarkFailed(failureCode, sanitized, now);
            attemptRepository.Update(attempt);
        }

        await queueRepository.SaveChangesAsync(cancellationToken);
    }

    private static async Task<AgentTaskRunAttempt?> ResolveAttemptAsync(
        AgentTaskRunQueueItem item,
        AgentTask? task,
        IRepository<AgentTaskRunAttempt> attemptRepository,
        CancellationToken cancellationToken)
    {
        if (item.RunAttemptId is not null)
        {
            var attempt = await attemptRepository.FirstOrDefaultAsync(
                new AgentTaskRunAttemptByIdSpec(item.RunAttemptId.Value),
                cancellationToken);
            if (attempt is not null)
            {
                return attempt;
            }
        }

        if (task?.ActiveRunAttemptId is not null)
        {
            var attempt = await attemptRepository.FirstOrDefaultAsync(
                new AgentTaskRunAttemptByIdSpec(task.ActiveRunAttemptId.Value),
                cancellationToken);
            if (attempt is not null)
            {
                return attempt;
            }
        }

        if (task is null)
        {
            return null;
        }

        var attempts = await attemptRepository.ListAsync(
            new AgentTaskRunAttemptsByTaskSpec(task.Id),
            cancellationToken);
        return attempts
            .OrderByDescending(attempt => attempt.AttemptNo)
            .ThenByDescending(attempt => attempt.StartedAt)
            .FirstOrDefault();
    }
}
