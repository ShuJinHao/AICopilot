using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class AgentTaskDtoQueryService(
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IAgentTaskRunQueueStore queueStore)
{
    public Task<AgentTaskDto> MapAsync(
        AgentTask task,
        CancellationToken cancellationToken)
    {
        return AgentTaskDtoComposer.MapAsync(
            task,
            workspaceRepository,
            approvalRepository,
            queueStore,
            cancellationToken);
    }

    public Task<AgentTaskDto> MapAsync(
        AgentTask task,
        CurrentUserAccess? currentUserAccess,
        CancellationToken cancellationToken)
    {
        return AgentTaskDtoComposer.MapAsync(
            task,
            workspaceRepository,
            approvalRepository,
            queueStore,
            currentUserAccess,
            cancellationToken);
    }

    public async Task<IReadOnlyCollection<AgentTaskDto>> MapManyAsync(
        IEnumerable<AgentTask> tasks,
        CurrentUserAccess? currentUserAccess,
        CancellationToken cancellationToken)
    {
        var dtos = new List<AgentTaskDto>();
        foreach (var task in tasks)
        {
            dtos.Add(await MapAsync(task, currentUserAccess, cancellationToken));
        }

        return dtos.ToArray();
    }
}

internal static class AgentTaskDtoComposer
{
    public static async Task<AgentTaskDto> MapAsync(
        AgentTask task,
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<ApprovalRequest> approvalRepository,
        IAgentTaskRunQueueStore? queueStore,
        CurrentUserAccess? currentUserAccess,
        CancellationToken cancellationToken)
    {
        var workspaceCode = await LoadWorkspaceCodeAsync(workspaceRepository, task, cancellationToken);
        var pendingApprovals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id, pendingOnly: true),
            cancellationToken);
        var activeQueueItem = queueStore is null
            ? null
            : await queueStore.FirstActiveByTaskAsync(task.Id, cancellationToken);
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
        IAgentTaskRunQueueStore? queueStore,
        CancellationToken cancellationToken)
    {
        return MapAsync(task, workspaceRepository, approvalRepository, queueStore, null, cancellationToken);
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
