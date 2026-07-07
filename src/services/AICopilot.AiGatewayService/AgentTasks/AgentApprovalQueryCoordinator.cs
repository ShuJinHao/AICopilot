using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class AgentApprovalQueryCoordinator(
    IRepository<AgentTask> taskRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
{
    public async Task<Result<IReadOnlyCollection<AgentApprovalRequestDto>>> GetPendingAsync(
        GetPendingAgentApprovalsQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return AgentApprovalAccess.MissingUser();
        }

        var accessResult = await AgentApprovalPermissions.LoadCurrentUserAccessAsync(
            currentUser,
            identityAccessService,
            cancellationToken);
        if (!accessResult.IsSuccess)
        {
            return Result.From(accessResult);
        }

        var currentAccess = accessResult.Value!;
        var approvals = await approvalRepository.ListAsync(
            new PendingApprovalRequestsSpec(),
            cancellationToken);
        var dtos = new List<AgentApprovalRequestDto>();
        foreach (var approval in approvals)
        {
            var task = await taskRepository.FirstOrDefaultAsync(
                new AgentTaskByIdSpec(approval.TaskId, includeSteps: true),
                cancellationToken);
            if (task is null || !CanViewPendingApproval(currentAccess, userId, task, approval))
            {
                continue;
            }

            var workspace = await AgentApprovalAccess.LoadWorkspaceAsync(workspaceRepository, task, cancellationToken);
            dtos.Add(AgentApprovalDtoMapper.Map(approval, task, workspace));
        }

        return Result.Success<IReadOnlyCollection<AgentApprovalRequestDto>>(dtos.ToArray());
    }

    public async Task<Result<IReadOnlyCollection<AgentApprovalRequestDto>>> GetByTaskAsync(
        GetAgentTaskApprovalsQuery request,
        CancellationToken cancellationToken)
    {
        var access = await AgentApprovalAccess.LoadTaskAsync(taskRepository, currentUser, request.TaskId, cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var task = access.Value!;
        var workspace = await AgentApprovalAccess.LoadWorkspaceAsync(workspaceRepository, task, cancellationToken);
        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id),
            cancellationToken);
        return Result.Success<IReadOnlyCollection<AgentApprovalRequestDto>>(
            approvals.Select(approval => AgentApprovalDtoMapper.Map(approval, task, workspace)).ToArray());
    }

    private static bool CanViewPendingApproval(
        CurrentUserAccess currentAccess,
        Guid userId,
        AgentTask task,
        ApprovalRequest approval)
    {
        var requiredPermission = AgentApprovalPermissions.GetRequiredDecisionPermission(approval.ApprovalType);
        if (!AgentApprovalPermissions.HasPermission(currentAccess, requiredPermission))
        {
            return false;
        }

        if (task.UserId == userId)
        {
            return true;
        }

        return AgentApprovalPermissions.AllowsCrossUserDecision(approval.ApprovalType);
    }
}
