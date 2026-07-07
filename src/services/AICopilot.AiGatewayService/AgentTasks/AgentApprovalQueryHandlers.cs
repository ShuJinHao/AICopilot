using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class GetPendingAgentApprovalsQueryHandler(
    AgentApprovalQueryCoordinator approvalQueryCoordinator)
    : IQueryHandler<GetPendingAgentApprovalsQuery, Result<IReadOnlyCollection<AgentApprovalRequestDto>>>
{
    public Task<Result<IReadOnlyCollection<AgentApprovalRequestDto>>> Handle(
        GetPendingAgentApprovalsQuery request,
        CancellationToken cancellationToken) =>
        approvalQueryCoordinator.GetPendingAsync(request, cancellationToken);
}

public sealed class GetAgentTaskApprovalsQueryHandler(
    AgentApprovalQueryCoordinator approvalQueryCoordinator)
    : IQueryHandler<GetAgentTaskApprovalsQuery, Result<IReadOnlyCollection<AgentApprovalRequestDto>>>
{
    public Task<Result<IReadOnlyCollection<AgentApprovalRequestDto>>> Handle(
        GetAgentTaskApprovalsQuery request,
        CancellationToken cancellationToken) =>
        approvalQueryCoordinator.GetByTaskAsync(request, cancellationToken);
}
