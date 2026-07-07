using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public interface IAgentTaskRunQueue
{
    Task<Result<AgentTaskRunQueueItem>> EnqueueAsync(
        AgentTask task,
        AgentTaskRunTriggerType triggerType,
        Guid requestedBy,
        CancellationToken cancellationToken,
        DateTimeOffset? availableAt = null);

    Task<IReadOnlyCollection<AgentTaskRunQueueItem>> CancelActiveAsync(
        AgentTask task,
        DateTimeOffset nowUtc,
        string safeMessage,
        CancellationToken cancellationToken);

    Task<Result<AgentTaskRunQueueItem?>> LeaseNextAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);
}

internal sealed class AgentTaskRunQueue(
    IAgentTaskRunQueueStore queueStore)
    : IAgentTaskRunQueue
{
    public async Task<Result<AgentTaskRunQueueItem>> EnqueueAsync(
        AgentTask task,
        AgentTaskRunTriggerType triggerType,
        Guid requestedBy,
        CancellationToken cancellationToken,
        DateTimeOffset? availableAt = null)
    {
        if (requestedBy == Guid.Empty)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        var active = await queueStore.FirstActiveByTaskAsync(task.Id, cancellationToken);
        if (active is not null)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentTaskRunInProgress,
                "Agent task already has an active queued or leased run."));
        }

        var now = DateTimeOffset.UtcNow;
        var item = new AgentTaskRunQueueItem(
            task.Id,
            triggerType,
            requestedBy,
            now,
            availableAt);
        queueStore.Add(item);
        await queueStore.SaveChangesAsync(cancellationToken);
        return Result.Success(item);
    }

    public async Task<IReadOnlyCollection<AgentTaskRunQueueItem>> CancelActiveAsync(
        AgentTask task,
        DateTimeOffset nowUtc,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        var activeItems = await queueStore.ListActiveByTaskAsync(task.Id, cancellationToken);
        foreach (var item in activeItems)
        {
            item.Cancel(nowUtc, safeMessage);
            queueStore.Update(item);
        }

        if (activeItems.Count > 0)
        {
            await queueStore.SaveChangesAsync(cancellationToken);
        }

        return activeItems;
    }

    public async Task<Result<AgentTaskRunQueueItem?>> LeaseNextAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var activeItems = await queueStore.ListActiveAsync(cancellationToken);
        var item = activeItems
            .Where(candidate => candidate.CanBeLeased(now))
            .OrderBy(candidate => candidate.AvailableAt)
            .ThenBy(candidate => candidate.CreatedAt)
            .FirstOrDefault();
        if (item is null)
        {
            return Result.Success<AgentTaskRunQueueItem?>(null);
        }

        item.AcquireLease(Guid.NewGuid(), leaseOwner, now, leaseDuration);
        queueStore.Update(item);
        await queueStore.SaveChangesAsync(cancellationToken);
        return Result.Success<AgentTaskRunQueueItem?>(item);
    }
}
