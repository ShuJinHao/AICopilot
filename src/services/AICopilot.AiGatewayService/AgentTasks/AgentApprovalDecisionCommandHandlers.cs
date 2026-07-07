using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class ApproveAgentApprovalCommandHandler(
    AgentApprovalDecisionCoordinator approvalDecisionCoordinator)
    : ICommandHandler<ApproveAgentApprovalCommand, Result<AgentApprovalRequestDto>>
{
    public async Task<Result<AgentApprovalRequestDto>> Handle(
        ApproveAgentApprovalCommand request,
        CancellationToken cancellationToken)
    {
        return await approvalDecisionCoordinator.ApproveAsync(
            request.Id,
            request.Comment,
            cancellationToken);
    }
}

public sealed class RejectAgentApprovalCommandHandler(
    AgentApprovalDecisionCoordinator approvalDecisionCoordinator)
    : ICommandHandler<RejectAgentApprovalCommand, Result<AgentApprovalRequestDto>>
{
    public async Task<Result<AgentApprovalRequestDto>> Handle(
        RejectAgentApprovalCommand request,
        CancellationToken cancellationToken)
    {
        return await approvalDecisionCoordinator.RejectAsync(
            request.Id,
            request.Comment,
            cancellationToken);
    }
}
