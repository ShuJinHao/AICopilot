using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.Approvals;

public sealed class PendingApprovalRequestByTaskAndTargetSpec : Specification<ApprovalRequest>
{
    public PendingApprovalRequestByTaskAndTargetSpec(AgentTaskId taskId, AgentApprovalType type, string targetId)
    {
        FilterCondition = request =>
            request.TaskId == taskId &&
            request.ApprovalType == type &&
            request.TargetId == targetId &&
            request.Status == AgentApprovalStatus.Pending;
    }
}

public sealed class ApprovalRequestByIdSpec : Specification<ApprovalRequest>
{
    public ApprovalRequestByIdSpec(ApprovalRequestId id)
    {
        FilterCondition = request => request.Id == id;
    }
}

public sealed class ApprovalRequestsByTasksSpec : Specification<ApprovalRequest>
{
    public ApprovalRequestsByTasksSpec(IReadOnlyCollection<AgentTaskId> taskIds, bool pendingOnly = false)
    {
        FilterCondition = request =>
            taskIds.Contains(request.TaskId) &&
            (!pendingOnly || request.Status == AgentApprovalStatus.Pending);
        SetOrderByDescending(request => request.CreatedAt);
    }
}

public sealed class PendingApprovalRequestsSpec : Specification<ApprovalRequest>
{
    public PendingApprovalRequestsSpec()
    {
        FilterCondition = request => request.Status == AgentApprovalStatus.Pending;
        SetOrderByDescending(request => request.CreatedAt);
    }
}

public sealed class ApprovalRequestsByTaskSpec : Specification<ApprovalRequest>
{
    public ApprovalRequestsByTaskSpec(AgentTaskId taskId, bool pendingOnly = false)
    {
        FilterCondition = request =>
            request.TaskId == taskId &&
            (!pendingOnly || request.Status == AgentApprovalStatus.Pending);
        SetOrderByDescending(request => request.CreatedAt);
    }
}
