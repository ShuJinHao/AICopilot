using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class ApproveAgentApprovalCommandHandler(
    IRepository<ApprovalRequest> approvalRepository,
    IRepository<AgentTask> taskRepository,
    IRepository<ArtifactWorkspace> workspaceRepository,
    AgentAuditRecorder auditRecorder,
    IAgentTaskRunQueue runQueue,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService,
    MessageTimelineProjectionWriter? timelineProjectionWriter = null)
    : ICommandHandler<ApproveAgentApprovalCommand, Result<AgentApprovalRequestDto>>
{
    public async Task<Result<AgentApprovalRequestDto>> Handle(
        ApproveAgentApprovalCommand request,
        CancellationToken cancellationToken)
    {
        return await DecideAsync(
            request.Id,
            request.Comment,
            isApproved: true,
            approvalRepository,
            taskRepository,
            workspaceRepository,
            auditRecorder,
            runQueue,
            currentUser,
            identityAccessService,
            timelineProjectionWriter,
            cancellationToken);
    }

    internal static async Task<Result<AgentApprovalRequestDto>> DecideAsync(
        Guid approvalId,
        string? comment,
        bool isApproved,
        IRepository<ApprovalRequest> approvalRepository,
        IRepository<AgentTask> taskRepository,
        IRepository<ArtifactWorkspace> workspaceRepository,
        AgentAuditRecorder auditRecorder,
        IAgentTaskRunQueue? runQueue,
        ICurrentUser currentUser,
        IIdentityAccessService identityAccessService,
        MessageTimelineProjectionWriter? timelineProjectionWriter,
        CancellationToken cancellationToken)
    {
        return await AgentApprovalDecisionWorkflow.DecideAsync(
            approvalId,
            comment,
            isApproved,
            approvalRepository,
            taskRepository,
            workspaceRepository,
            auditRecorder,
            runQueue,
            currentUser,
            identityAccessService,
            timelineProjectionWriter,
            cancellationToken);
    }
}

public sealed class RejectAgentApprovalCommandHandler(
    IRepository<ApprovalRequest> approvalRepository,
    IRepository<AgentTask> taskRepository,
    IRepository<ArtifactWorkspace> workspaceRepository,
    AgentAuditRecorder auditRecorder,
    IAgentTaskRunQueue runQueue,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService,
    MessageTimelineProjectionWriter? timelineProjectionWriter = null)
    : ICommandHandler<RejectAgentApprovalCommand, Result<AgentApprovalRequestDto>>
{
    public async Task<Result<AgentApprovalRequestDto>> Handle(
        RejectAgentApprovalCommand request,
        CancellationToken cancellationToken)
    {
        return await AgentApprovalDecisionWorkflow.DecideAsync(
            request.Id,
            request.Comment,
            isApproved: false,
            approvalRepository,
            taskRepository,
            workspaceRepository,
            auditRecorder,
            runQueue,
            currentUser,
            identityAccessService,
            timelineProjectionWriter,
            cancellationToken);
    }
}
