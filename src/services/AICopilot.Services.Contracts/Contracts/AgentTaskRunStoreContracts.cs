using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;

namespace AICopilot.Services.Contracts;

public interface IAgentTaskRunQueueStore
{
    Task<AgentTaskRunQueueItem?> FirstActiveByTaskAsync(
        AgentTaskId taskId,
        CancellationToken cancellationToken = default);

    Task<AgentTaskRunQueueItem?> FirstByIdAsync(
        AgentTaskRunQueueItemId id,
        CancellationToken cancellationToken = default);

    Task<List<AgentTaskRunQueueItem>> ListActiveByTaskAsync(
        AgentTaskId taskId,
        CancellationToken cancellationToken = default);

    Task<List<AgentTaskRunQueueItem>> ListActiveAsync(CancellationToken cancellationToken = default);

    Task<List<AgentTaskRunQueueItem>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<List<AgentTaskRunQueueItem>> ListByTaskAsync(
        AgentTaskId taskId,
        CancellationToken cancellationToken = default);

    AgentTaskRunQueueItem Add(AgentTaskRunQueueItem item);

    void Update(AgentTaskRunQueueItem item);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IAgentTaskRunAttemptStore
{
    Task<AgentTaskRunAttempt?> FirstByIdAsync(
        AgentTaskRunAttemptId id,
        CancellationToken cancellationToken = default);

    Task<List<AgentTaskRunAttempt>> ListByTaskAsync(
        AgentTaskId taskId,
        CancellationToken cancellationToken = default);

    AgentTaskRunAttempt Add(AgentTaskRunAttempt attempt);

    void Update(AgentTaskRunAttempt attempt);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public enum AgentTaskCancellationDisposition
{
    Cancelled = 0,
    ReconciliationRequired = 1,
    AlreadyTerminal = 2,
    StateConflict = 3
}

public sealed record AgentTaskCancellationQueueItem(
    AgentTaskRunQueueItem Item,
    AgentTaskRunQueueStatus PreviousStatus);

public sealed record AgentTaskCancellationCheckpoint(
    AgentTaskCancellationDisposition Disposition,
    IReadOnlyCollection<AgentTaskCancellationQueueItem> QueueItems,
    string SafeMessage);

public interface IAgentTaskCancellationStore
{
    Task<AgentTaskCancellationCheckpoint> RequestAsync(
        AgentTaskId taskId,
        DateTimeOffset requestedAtUtc,
        string safeMessage,
        CancellationToken cancellationToken = default);
}
