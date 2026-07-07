using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.AgentTasks;

public sealed class AgentWorkerHeartbeat : BaseEntity<AgentWorkerHeartbeatId>
{
    private AgentWorkerHeartbeat()
    {
    }

    public AgentWorkerHeartbeat(
        string workerId,
        string workerName,
        DateTimeOffset nowUtc,
        string workspaceRootHash,
        string version)
    {
        Id = AgentWorkerHeartbeatId.New();
        WorkerId = NormalizeRequired(workerId, 160, nameof(workerId));
        WorkerName = NormalizeRequired(workerName, 160, nameof(workerName));
        StartedAt = nowUtc;
        LastSeenAt = nowUtc;
        WorkspaceRootHash = NormalizeRequired(workspaceRootHash, 128, nameof(workspaceRootHash));
        Version = NormalizeRequired(version, 80, nameof(version));
    }

    public string WorkerId { get; private set; } = string.Empty;

    public string WorkerName { get; private set; } = string.Empty;

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public AgentTaskRunQueueItemId? ActiveQueueItemId { get; private set; }

    public AgentTaskId? ActiveTaskId { get; private set; }

    public string WorkspaceRootHash { get; private set; } = string.Empty;

    public string Version { get; private set; } = string.Empty;

    public void MarkSeen(
        DateTimeOffset nowUtc,
        string workerName,
        string workspaceRootHash,
        string version,
        AgentTaskRunQueueItemId? activeQueueItemId,
        AgentTaskId? activeTaskId)
    {
        WorkerName = NormalizeRequired(workerName, 160, nameof(workerName));
        LastSeenAt = nowUtc;
        WorkspaceRootHash = NormalizeRequired(workspaceRootHash, 128, nameof(workspaceRootHash));
        Version = NormalizeRequired(version, 80, nameof(version));
        ActiveQueueItemId = activeQueueItemId;
        ActiveTaskId = activeTaskId;
    }

    private static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
    }
}
