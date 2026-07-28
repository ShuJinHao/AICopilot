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
    IAgentTaskRunQueueStore queueStore,
    IAgentTaskRunAttemptStore? runAttemptStore = null)
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
            runAttemptStore,
            null,
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
            runAttemptStore,
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
        IAgentTaskRunAttemptStore? runAttemptStore,
        CurrentUserAccess? currentUserAccess,
        CancellationToken cancellationToken)
    {
        var workspace = await LoadWorkspaceAsync(workspaceRepository, task, cancellationToken);
        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id),
            cancellationToken);
        var activeQueueItem = queueStore is null
            ? null
            : await queueStore.FirstActiveByTaskAsync(task.Id, cancellationToken);
        var attempts = runAttemptStore is null
            ? Array.Empty<AgentTaskRunAttempt>()
            : (await runAttemptStore.ListByTaskAsync(task.Id, cancellationToken)).ToArray();
        var finalCheckpointState = runAttemptStore is null
            ? null
            : AgentFinalizationCheckpointStateValidator.ValidatePaused(
                task,
                workspace,
                approvals,
                attempts);
        var canApproveFinal =
            finalCheckpointState is { IsSuccess: true } &&
            finalCheckpointState.Value!.Phase == AgentFinalizationCheckpointPhase.PendingApproval &&
            AgentApprovalPermissions.HasPermission(
                currentUserAccess,
                AgentApprovalPermissions.ApproveFinalOutput);
        var canFinalizeWorkspace =
            finalCheckpointState is { IsSuccess: true } &&
            finalCheckpointState.Value!.Phase == AgentFinalizationCheckpointPhase.Approved &&
            AgentApprovalPermissions.HasPermission(
                currentUserAccess,
                AgentApprovalPermissions.FinalizeWorkspace);
        return AgentTaskDtoMapper.Map(
            task,
            workspace?.WorkspaceCode,
            approvals.Count(approval => approval.Status == AgentApprovalStatus.Pending),
            activeQueueItem,
            canApproveFinal,
            canFinalizeWorkspace);
    }

    public static Task<AgentTaskDto> MapAsync(
        AgentTask task,
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<ApprovalRequest> approvalRepository,
        IAgentTaskRunQueueStore? queueStore,
        CancellationToken cancellationToken)
    {
        return MapAsync(task, workspaceRepository, approvalRepository, queueStore, null, null, cancellationToken);
    }

    public static Task<AgentTaskDto> MapAsync(
        AgentTask task,
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<ApprovalRequest> approvalRepository,
        CancellationToken cancellationToken)
    {
        return MapAsync(task, workspaceRepository, approvalRepository, null, null, null, cancellationToken);
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

    private static async Task<ArtifactWorkspace?> LoadWorkspaceAsync(
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        AgentTask task,
        CancellationToken cancellationToken)
    {
        if (task.WorkspaceId is null)
        {
            return null;
        }

        return await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByIdSpec(task.WorkspaceId.Value, includeArtifacts: true),
            cancellationToken);
    }

}
