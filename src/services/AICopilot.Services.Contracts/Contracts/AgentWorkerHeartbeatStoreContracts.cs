using AICopilot.Core.AiGateway.Aggregates.AgentTasks;

namespace AICopilot.Services.Contracts;

public interface IAgentWorkerHeartbeatStore
{
    Task<AgentWorkerHeartbeat?> FirstByWorkerIdAsync(
        string workerId,
        CancellationToken cancellationToken = default);

    Task<List<AgentWorkerHeartbeat>> ListAllAsync(CancellationToken cancellationToken = default);

    AgentWorkerHeartbeat Add(AgentWorkerHeartbeat heartbeat);

    void Update(AgentWorkerHeartbeat heartbeat);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
