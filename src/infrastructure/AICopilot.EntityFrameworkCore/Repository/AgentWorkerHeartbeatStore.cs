using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

public sealed class AgentWorkerHeartbeatStore(
    AiGatewayDbContext dbContext,
    RepositoryPersistenceCommitter persistenceCommitter)
    : IAgentWorkerHeartbeatStore
{
    public async Task<AgentWorkerHeartbeat?> FirstByWorkerIdAsync(
        string workerId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.AgentWorkerHeartbeats
            .FirstOrDefaultAsync(heartbeat => heartbeat.WorkerId == workerId, cancellationToken);
    }

    public async Task<List<AgentWorkerHeartbeat>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.AgentWorkerHeartbeats
            .OrderByDescending(heartbeat => heartbeat.LastSeenAt)
            .ToListAsync(cancellationToken);
    }

    public AgentWorkerHeartbeat Add(AgentWorkerHeartbeat heartbeat)
    {
        dbContext.AgentWorkerHeartbeats.Add(heartbeat);
        return heartbeat;
    }

    public void Update(AgentWorkerHeartbeat heartbeat)
    {
        dbContext.AgentWorkerHeartbeats.Update(heartbeat);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return persistenceCommitter.SaveChangesAsync(dbContext, cancellationToken);
    }
}
