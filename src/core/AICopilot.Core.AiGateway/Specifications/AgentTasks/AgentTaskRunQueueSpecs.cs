using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.AgentTasks;

public sealed class ActiveAgentTaskRunQueueItemByTaskSpec : Specification<AgentTaskRunQueueItem>
{
    public ActiveAgentTaskRunQueueItemByTaskSpec(AgentTaskId taskId)
    {
        FilterCondition = item => item.TaskId == taskId &&
                                  (item.Status == AgentTaskRunQueueStatus.Queued ||
                                   item.Status == AgentTaskRunQueueStatus.Leased);
        SetOrderByDescending(item => item.CreatedAt);
    }
}

public sealed class AgentTaskRunQueueItemsByTaskSpec : Specification<AgentTaskRunQueueItem>
{
    public AgentTaskRunQueueItemsByTaskSpec(AgentTaskId taskId)
    {
        FilterCondition = item => item.TaskId == taskId;
        SetOrderByDescending(item => item.CreatedAt);
    }
}

public sealed class AgentTaskRunQueueItemByIdSpec : Specification<AgentTaskRunQueueItem>
{
    public AgentTaskRunQueueItemByIdSpec(AgentTaskRunQueueItemId id)
    {
        FilterCondition = item => item.Id == id;
    }
}

public sealed class AgentTaskRunQueueActiveItemsSpec : Specification<AgentTaskRunQueueItem>
{
    public AgentTaskRunQueueActiveItemsSpec()
    {
        FilterCondition = item => item.Status == AgentTaskRunQueueStatus.Queued ||
                                  item.Status == AgentTaskRunQueueStatus.Leased;
        SetOrderBy(item => item.AvailableAt);
    }
}

public sealed class AgentTaskRunQueueAllItemsSpec : Specification<AgentTaskRunQueueItem>
{
    public AgentTaskRunQueueAllItemsSpec()
    {
        SetOrderByDescending(item => item.CreatedAt);
    }
}

public sealed class AgentWorkerHeartbeatByWorkerIdSpec : Specification<AgentWorkerHeartbeat>
{
    public AgentWorkerHeartbeatByWorkerIdSpec(string workerId)
    {
        FilterCondition = heartbeat => heartbeat.WorkerId == workerId;
    }
}

public sealed class AgentWorkerHeartbeatAllSpec : Specification<AgentWorkerHeartbeat>
{
    public AgentWorkerHeartbeatAllSpec()
    {
        SetOrderByDescending(heartbeat => heartbeat.LastSeenAt);
    }
}
