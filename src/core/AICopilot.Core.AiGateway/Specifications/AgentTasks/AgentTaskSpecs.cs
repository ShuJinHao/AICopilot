using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.AgentTasks;

public sealed class AgentTaskByIdForUserSpec : Specification<AgentTask>
{
    public AgentTaskByIdForUserSpec(AgentTaskId id, Guid userId, bool includeSteps = false)
    {
        FilterCondition = task => task.Id == id && task.UserId == userId;
        if (includeSteps)
        {
            AddInclude(task => task.Steps);
        }
    }
}

public sealed class AgentTaskByIdSpec : Specification<AgentTask>
{
    public AgentTaskByIdSpec(AgentTaskId id, bool includeSteps = false)
    {
        FilterCondition = task => task.Id == id;
        if (includeSteps)
        {
            AddInclude(task => task.Steps);
        }
    }
}

public sealed class AgentTasksBySessionForUserSpec : Specification<AgentTask>
{
    public AgentTasksBySessionForUserSpec(SessionId sessionId, Guid userId, bool includeSteps = false)
    {
        FilterCondition = task => task.SessionId == sessionId && task.UserId == userId;
        if (includeSteps)
        {
            AddInclude(task => task.Steps);
        }

        SetOrderByDescending(task => task.UpdatedAt);
    }
}

public sealed class AgentTasksByUserSpec : Specification<AgentTask>
{
    public AgentTasksByUserSpec(Guid userId, bool includeSteps = false)
    {
        FilterCondition = task => task.UserId == userId;
        SetOrderByDescending(task => task.UpdatedAt);
        if (includeSteps)
        {
            AddInclude(task => task.Steps);
        }
    }
}
