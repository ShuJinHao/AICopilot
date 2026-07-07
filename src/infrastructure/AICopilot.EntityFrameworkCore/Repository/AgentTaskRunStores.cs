using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

public sealed class AgentTaskRunQueueStore(
    AiGatewayDbContext dbContext,
    AuditTransactionCoordinator transactionCoordinator)
    : IAgentTaskRunQueueStore
{
    public async Task<AgentTaskRunQueueItem?> FirstActiveByTaskAsync(
        AgentTaskId taskId,
        CancellationToken cancellationToken = default)
    {
        return await ActiveByTask(taskId)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<AgentTaskRunQueueItem?> FirstByIdAsync(
        AgentTaskRunQueueItemId id,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.AgentTaskRunQueueItems
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<List<AgentTaskRunQueueItem>> ListActiveByTaskAsync(
        AgentTaskId taskId,
        CancellationToken cancellationToken = default)
    {
        return await ActiveByTask(taskId)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AgentTaskRunQueueItem>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.AgentTaskRunQueueItems
            .Where(item => item.Status == AgentTaskRunQueueStatus.Queued ||
                           item.Status == AgentTaskRunQueueStatus.Leased)
            .OrderBy(item => item.AvailableAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AgentTaskRunQueueItem>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.AgentTaskRunQueueItems
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AgentTaskRunQueueItem>> ListByTaskAsync(
        AgentTaskId taskId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.AgentTaskRunQueueItems
            .Where(item => item.TaskId == taskId)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public AgentTaskRunQueueItem Add(AgentTaskRunQueueItem item)
    {
        dbContext.AgentTaskRunQueueItems.Add(item);
        return item;
    }

    public void Update(AgentTaskRunQueueItem item)
    {
        dbContext.AgentTaskRunQueueItems.Update(item);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return transactionCoordinator.SaveChangesAsync(dbContext, cancellationToken);
    }

    private IQueryable<AgentTaskRunQueueItem> ActiveByTask(AgentTaskId taskId)
    {
        return dbContext.AgentTaskRunQueueItems
            .Where(item => item.TaskId == taskId)
            .Where(item => item.Status == AgentTaskRunQueueStatus.Queued ||
                           item.Status == AgentTaskRunQueueStatus.Leased);
    }
}

public sealed class AgentTaskRunAttemptStore(
    AiGatewayDbContext dbContext,
    AuditTransactionCoordinator transactionCoordinator)
    : IAgentTaskRunAttemptStore
{
    public async Task<AgentTaskRunAttempt?> FirstByIdAsync(
        AgentTaskRunAttemptId id,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.AgentTaskRunAttempts
            .FirstOrDefaultAsync(attempt => attempt.Id == id, cancellationToken);
    }

    public async Task<List<AgentTaskRunAttempt>> ListByTaskAsync(
        AgentTaskId taskId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.AgentTaskRunAttempts
            .Where(attempt => attempt.TaskId == taskId)
            .OrderByDescending(attempt => attempt.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public AgentTaskRunAttempt Add(AgentTaskRunAttempt attempt)
    {
        dbContext.AgentTaskRunAttempts.Add(attempt);
        return attempt;
    }

    public void Update(AgentTaskRunAttempt attempt)
    {
        dbContext.AgentTaskRunAttempts.Update(attempt);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return transactionCoordinator.SaveChangesAsync(dbContext, cancellationToken);
    }
}
