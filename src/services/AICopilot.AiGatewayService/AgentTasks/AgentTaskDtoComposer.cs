using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentTaskDtoComposer
{
    public static async Task<AgentTaskDto> MapAsync(
        AgentTask task,
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<ApprovalRequest> approvalRepository,
        IReadRepository<AgentTaskRunQueueItem>? queueRepository,
        CurrentUserAccess? currentUserAccess,
        CancellationToken cancellationToken)
    {
        var workspaceCode = await LoadWorkspaceCodeAsync(workspaceRepository, task, cancellationToken);
        var pendingApprovals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id, pendingOnly: true),
            cancellationToken);
        var activeQueueItem = queueRepository is null
            ? null
            : await queueRepository.FirstOrDefaultAsync(
                new ActiveAgentTaskRunQueueItemByTaskSpec(task.Id),
                cancellationToken);
        var canApproveFinal = AgentApprovalPermissions.HasPermission(
            currentUserAccess,
            AgentApprovalPermissions.ApproveFinalOutput);
        bool? canSubmitFinalReview = currentUserAccess is null
            ? null
            : AgentApprovalPermissions.HasPermission(
                currentUserAccess,
                AgentApprovalPermissions.SubmitFinalReview);
        return AgentTaskDtoMapper.Map(
            task,
            workspaceCode,
            pendingApprovals.Count,
            activeQueueItem,
            canApproveFinal,
            canSubmitFinalReview);
    }

    public static Task<AgentTaskDto> MapAsync(
        AgentTask task,
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<ApprovalRequest> approvalRepository,
        IReadRepository<AgentTaskRunQueueItem>? queueRepository,
        CancellationToken cancellationToken)
    {
        return MapAsync(task, workspaceRepository, approvalRepository, queueRepository, null, cancellationToken);
    }

    public static Task<AgentTaskDto> MapAsync(
        AgentTask task,
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<ApprovalRequest> approvalRepository,
        CancellationToken cancellationToken)
    {
        return MapAsync(task, workspaceRepository, approvalRepository, null, null, cancellationToken);
    }

    public static async Task<string?> LoadWorkspaceCodeAsync(
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        AgentTask task,
        CancellationToken cancellationToken)
    {
        if (task.WorkspaceId is null)
        {
            return null;
        }

        var workspace = await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByIdSpec(task.WorkspaceId.Value),
            cancellationToken);
        return workspace?.WorkspaceCode;
    }
}
