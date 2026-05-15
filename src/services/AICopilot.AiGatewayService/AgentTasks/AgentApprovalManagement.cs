using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed record AgentApprovalRequestDto(
    Guid Id,
    Guid TaskId,
    string? WorkspaceCode,
    string Type,
    string TargetId,
    string TargetName,
    string RiskLevel,
    string Status,
    string? Reason,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    Guid? DecidedBy);

public sealed record AgentApprovalDecisionRequest(string? Comment = null);

[AuthorizeRequirement("AiGateway.GetAgentTask")]
public sealed record GetPendingAgentApprovalsQuery : IQuery<Result<IReadOnlyCollection<AgentApprovalRequestDto>>>;

[AuthorizeRequirement("AiGateway.GetAgentTask")]
public sealed record GetAgentTaskApprovalsQuery(Guid TaskId) : IQuery<Result<IReadOnlyCollection<AgentApprovalRequestDto>>>;

[AuthorizeRequirement("AiGateway.ApproveAgentTaskPlan")]
public sealed record ApproveAgentApprovalCommand(Guid Id, string? Comment = null) : ICommand<Result<AgentApprovalRequestDto>>;

[AuthorizeRequirement("AiGateway.ApproveAgentTaskPlan")]
public sealed record RejectAgentApprovalCommand(Guid Id, string? Comment = null) : ICommand<Result<AgentApprovalRequestDto>>;

public sealed class GetPendingAgentApprovalsQueryHandler(
    IReadRepository<AgentTask> taskRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    ICurrentUser currentUser)
    : IQueryHandler<GetPendingAgentApprovalsQuery, Result<IReadOnlyCollection<AgentApprovalRequestDto>>>
{
    public async Task<Result<IReadOnlyCollection<AgentApprovalRequestDto>>> Handle(
        GetPendingAgentApprovalsQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return AgentApprovalAccess.MissingUser();
        }

        var tasks = await taskRepository.ListAsync(
            new AgentTasksByUserSpec(userId, includeSteps: true),
            cancellationToken);
        var dtos = new List<AgentApprovalRequestDto>();
        foreach (var task in tasks)
        {
            var approvals = await approvalRepository.ListAsync(
                new ApprovalRequestsByTaskSpec(task.Id, pendingOnly: true),
                cancellationToken);
            if (approvals.Count == 0)
            {
                continue;
            }

            var workspace = await AgentApprovalAccess.LoadWorkspaceAsync(workspaceRepository, task, cancellationToken);
            dtos.AddRange(approvals.Select(approval => AgentApprovalDtoMapper.Map(approval, task, workspace)));
        }

        return Result.Success<IReadOnlyCollection<AgentApprovalRequestDto>>(dtos.ToArray());
    }
}

public sealed class GetAgentTaskApprovalsQueryHandler(
    IRepository<AgentTask> taskRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    ICurrentUser currentUser)
    : IQueryHandler<GetAgentTaskApprovalsQuery, Result<IReadOnlyCollection<AgentApprovalRequestDto>>>
{
    public async Task<Result<IReadOnlyCollection<AgentApprovalRequestDto>>> Handle(
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
}

public sealed class ApproveAgentApprovalCommandHandler(
    IRepository<ApprovalRequest> approvalRepository,
    IRepository<AgentTask> taskRepository,
    IRepository<ArtifactWorkspace> workspaceRepository,
    AgentAuditRecorder auditRecorder,
    ICurrentUser currentUser)
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
            currentUser,
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
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return AgentApprovalAccess.MissingUser();
        }

        if (approvalId == Guid.Empty)
        {
            return Result.Invalid("Approval request id is required.");
        }

        var approval = await approvalRepository.FirstOrDefaultAsync(
            new ApprovalRequestByIdSpec(new ApprovalRequestId(approvalId)),
            cancellationToken);
        if (approval is null)
        {
            return Result.NotFound();
        }

        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdForUserSpec(approval.TaskId, userId, includeSteps: true),
            cancellationToken);
        if (task is null)
        {
            return Result.NotFound();
        }

        var workspace = await AgentApprovalAccess.LoadWorkspaceAsync(workspaceRepository, task, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (isApproved)
        {
            approval.Approve(userId, comment, now);
            ApplyApproval(task, workspace, approval, now);
        }
        else
        {
            approval.Reject(userId, comment, now);
            ApplyRejection(task, approval, comment, now);
        }

        approvalRepository.Update(approval);
        taskRepository.Update(task);
        if (workspace is not null)
        {
            workspaceRepository.Update(workspace);
        }

        await auditRecorder.RecordApprovalDecisionAsync(
            approval,
            task,
            isApproved ? AuditResults.Succeeded : AuditResults.Rejected,
            BuildDecisionSummary(approval, isApproved, comment),
            cancellationToken);
        await approvalRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(AgentApprovalDtoMapper.Map(approval, task, workspace));
    }

    private static void ApplyApproval(
        AgentTask task,
        ArtifactWorkspace? workspace,
        ApprovalRequest approval,
        DateTimeOffset now)
    {
        switch (approval.ApprovalType)
        {
            case AgentApprovalType.Plan:
                if (task.Status == AgentTaskStatus.WaitingPlanApproval)
                {
                    task.ApprovePlan(now);
                }

                break;
            case AgentApprovalType.ToolCall:
                var step = AgentApprovalDtoMapper.FindStep(task, approval.TargetId);
                if (step?.Status == AgentStepStatus.WaitingApproval)
                {
                    step.Approve();
                }

                if (task.Status == AgentTaskStatus.WaitingToolApproval)
                {
                    task.Start(now);
                }

                break;
            case AgentApprovalType.Artifact:
                if (workspace is not null &&
                    Guid.TryParse(approval.TargetId, out var artifactId))
                {
                    var artifact = workspace.Artifacts.FirstOrDefault(item => item.Id == new ArtifactId(artifactId));
                    if (artifact?.Status is ArtifactStatus.Draft or ArtifactStatus.Reviewing)
                    {
                        artifact.Approve(now);
                    }
                }

                break;
            case AgentApprovalType.FinalOutput:
                if (task.Status == AgentTaskStatus.WorkspaceReady)
                {
                    task.WaitForFinalApproval(now);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(approval.ApprovalType), approval.ApprovalType, "Unknown approval type.");
        }
    }

    private static void ApplyRejection(
        AgentTask task,
        ApprovalRequest approval,
        string? comment,
        DateTimeOffset now)
    {
        var reason = string.IsNullOrWhiteSpace(comment)
            ? $"Approval request {approval.Id.Value} was rejected."
            : comment.Trim();
        if (approval.ApprovalType == AgentApprovalType.ToolCall)
        {
            var step = AgentApprovalDtoMapper.FindStep(task, approval.TargetId);
            if (step is not null && step.Status != AgentStepStatus.Completed)
            {
                step.Fail(reason, now);
            }
        }

        task.Reject(reason, now);
    }

    private static string BuildDecisionSummary(
        ApprovalRequest approval,
        bool isApproved,
        string? comment)
    {
        var action = isApproved ? "approved" : "rejected";
        return string.IsNullOrWhiteSpace(comment)
            ? $"Agent {approval.ApprovalType} approval {action}."
            : $"Agent {approval.ApprovalType} approval {action}: {comment.Trim()}";
    }
}

public sealed class RejectAgentApprovalCommandHandler(
    IRepository<ApprovalRequest> approvalRepository,
    IRepository<AgentTask> taskRepository,
    IRepository<ArtifactWorkspace> workspaceRepository,
    AgentAuditRecorder auditRecorder,
    ICurrentUser currentUser)
    : ICommandHandler<RejectAgentApprovalCommand, Result<AgentApprovalRequestDto>>
{
    public async Task<Result<AgentApprovalRequestDto>> Handle(
        RejectAgentApprovalCommand request,
        CancellationToken cancellationToken)
    {
        return await ApproveAgentApprovalCommandHandler.DecideAsync(
            request.Id,
            request.Comment,
            isApproved: false,
            approvalRepository,
            taskRepository,
            workspaceRepository,
            auditRecorder,
            currentUser,
            cancellationToken);
    }
}

internal static class AgentApprovalAccess
{
    public static Result MissingUser()
    {
        return Result.Unauthorized(new ApiProblemDescriptor(
            AuthProblemCodes.Unauthorized,
            "Current user id is missing or invalid."));
    }

    public static async Task<Result<AgentTask>> LoadTaskAsync(
        IRepository<AgentTask> taskRepository,
        ICurrentUser currentUser,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return MissingUser();
        }

        if (taskId == Guid.Empty)
        {
            return Result.Invalid("Agent task id is required.");
        }

        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdForUserSpec(new AgentTaskId(taskId), userId, includeSteps: true),
            cancellationToken);
        return task is null ? Result.NotFound() : Result.Success(task);
    }

    public static async Task<ArtifactWorkspace?> LoadWorkspaceAsync(
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

internal static class AgentApprovalDtoMapper
{
    public static AgentApprovalRequestDto Map(
        ApprovalRequest approval,
        AgentTask task,
        ArtifactWorkspace? workspace)
    {
        return new AgentApprovalRequestDto(
            approval.Id,
            task.Id,
            workspace?.WorkspaceCode,
            approval.ApprovalType.ToString(),
            approval.TargetId,
            ResolveTargetName(approval, task, workspace),
            ResolveRiskLevel(approval, task),
            approval.Status.ToString(),
            ResolveReason(approval),
            approval.CreatedAt,
            approval.ApprovedAt,
            approval.ApprovedBy);
    }

    public static AgentStep? FindStep(AgentTask task, string targetId)
    {
        return Guid.TryParse(targetId, out var stepId)
            ? task.Steps.FirstOrDefault(step => step.Id == new AgentStepId(stepId))
            : null;
    }

    public static string ResolveToolRisk(string? toolCode)
    {
        return toolCode switch
        {
            "query_cloud_data_readonly" => AgentTaskRiskLevel.Medium.ToString(),
            "generate_pdf" or "generate_pptx" or "generate_xlsx" or "finalize_artifacts" => AgentTaskRiskLevel.High.ToString(),
            _ => AgentTaskRiskLevel.Low.ToString()
        };
    }

    private static string ResolveTargetName(
        ApprovalRequest approval,
        AgentTask task,
        ArtifactWorkspace? workspace)
    {
        if (approval.ApprovalType == AgentApprovalType.Plan)
        {
            return task.Title;
        }

        if (approval.ApprovalType == AgentApprovalType.ToolCall)
        {
            var step = FindStep(task, approval.TargetId);
            return step is null
                ? approval.TargetId
                : string.IsNullOrWhiteSpace(step.ToolCode)
                    ? step.Title
                    : $"{step.StepIndex}. {step.ToolCode}";
        }

        if (approval.ApprovalType == AgentApprovalType.Artifact &&
            workspace is not null &&
            Guid.TryParse(approval.TargetId, out var artifactId))
        {
            return workspace.Artifacts.FirstOrDefault(item => item.Id == new ArtifactId(artifactId))?.Name
                   ?? approval.TargetId;
        }

        return workspace?.WorkspaceCode ?? task.TaskCode;
    }

    private static string ResolveRiskLevel(ApprovalRequest approval, AgentTask task)
    {
        if (approval.ApprovalType != AgentApprovalType.ToolCall)
        {
            return task.RiskLevel.ToString();
        }

        var step = FindStep(task, approval.TargetId);
        return ResolveToolRisk(step?.ToolCode);
    }

    private static string? ResolveReason(ApprovalRequest approval)
    {
        if (!string.IsNullOrWhiteSpace(approval.ApprovalComment))
        {
            return approval.ApprovalComment;
        }

        return approval.Status == AgentApprovalStatus.Pending
            ? $"Waiting for {approval.ApprovalType} approval."
            : null;
    }
}
