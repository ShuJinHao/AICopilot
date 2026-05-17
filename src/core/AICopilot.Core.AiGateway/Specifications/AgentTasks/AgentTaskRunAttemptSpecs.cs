using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.AgentTasks;

public sealed class AgentTaskRunAttemptsByTaskSpec : Specification<AgentTaskRunAttempt>
{
    public AgentTaskRunAttemptsByTaskSpec(AgentTaskId taskId)
    {
        FilterCondition = attempt => attempt.TaskId == taskId;
        SetOrderByDescending(attempt => attempt.StartedAt);
    }
}

public sealed class AgentTaskRunAttemptByIdSpec : Specification<AgentTaskRunAttempt>
{
    public AgentTaskRunAttemptByIdSpec(AgentTaskRunAttemptId id)
    {
        FilterCondition = attempt => attempt.Id == id;
    }
}
