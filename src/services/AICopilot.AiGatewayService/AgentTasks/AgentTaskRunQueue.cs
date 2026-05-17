using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.SharedKernel.Repository;
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

    Task CancelActiveAsync(
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
    IRepository<AgentTaskRunQueueItem> queueRepository)
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

        var active = await queueRepository.FirstOrDefaultAsync(
            new ActiveAgentTaskRunQueueItemByTaskSpec(task.Id),
            cancellationToken);
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
        queueRepository.Add(item);
        await queueRepository.SaveChangesAsync(cancellationToken);
        return Result.Success(item);
    }

    public async Task CancelActiveAsync(
        AgentTask task,
        DateTimeOffset nowUtc,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        var activeItems = await queueRepository.ListAsync(
            new ActiveAgentTaskRunQueueItemByTaskSpec(task.Id),
            cancellationToken);
        foreach (var item in activeItems)
        {
            item.Cancel(nowUtc, safeMessage);
            queueRepository.Update(item);
        }

        if (activeItems.Count > 0)
        {
            await queueRepository.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<Result<AgentTaskRunQueueItem?>> LeaseNextAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var activeItems = await queueRepository.ListAsync(
            new AgentTaskRunQueueActiveItemsSpec(),
            cancellationToken);
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
        queueRepository.Update(item);
        await queueRepository.SaveChangesAsync(cancellationToken);
        return Result.Success<AgentTaskRunQueueItem?>(item);
    }
}
