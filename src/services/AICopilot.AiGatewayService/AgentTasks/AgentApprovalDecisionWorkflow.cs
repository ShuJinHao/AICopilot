using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentApprovalDecisionWorkflow
{
    public static async Task<Result<AgentApprovalRequestDto>> DecideAsync(
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

        var taskResult = await LoadDecisionTaskAsync(
            approval,
            userId,
            taskRepository,
            currentUser,
            identityAccessService,
            cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
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
        if (timelineProjectionWriter is not null)
        {
            await timelineProjectionWriter.StageApprovalDecidedAsync(task, approval, cancellationToken);
        }

        await approvalRepository.SaveChangesAsync(cancellationToken);

        if (isApproved &&
            approval.ApprovalType == AgentApprovalType.ToolCall &&
            runQueue is not null)
        {
            _ = await runQueue.EnqueueAsync(
                task,
                AgentTaskRunTriggerType.ApprovalResume,
                userId,
                cancellationToken);
        }

        return Result.Success(AgentApprovalDtoMapper.Map(approval, task, workspace));
    }

    private static async Task<Result<AgentTask>> LoadDecisionTaskAsync(
        ApprovalRequest approval,
        Guid userId,
        IReadRepository<AgentTask> taskRepository,
        ICurrentUser currentUser,
        IIdentityAccessService identityAccessService,
        CancellationToken cancellationToken)
    {
        var accessResult = await AgentApprovalPermissions.LoadCurrentUserAccessAsync(
            currentUser,
            identityAccessService,
            cancellationToken);
        if (!accessResult.IsSuccess)
        {
            return Result.From(accessResult);
        }

        var requiredPermission = AgentApprovalPermissions.GetRequiredDecisionPermission(approval.ApprovalType);
        if (!AgentApprovalPermissions.HasPermission(accessResult.Value, requiredPermission))
        {
            return AgentApprovalPermissions.ForbiddenMissing(requiredPermission);
        }

        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdSpec(approval.TaskId, includeSteps: true),
            cancellationToken);
        if (task is null)
        {
            return Result.NotFound();
        }

        if (task.UserId == userId)
        {
            return Result.Success(task);
        }

        return AgentApprovalPermissions.AllowsCrossUserDecision(approval.ApprovalType)
            ? Result.Success(task)
            : Result.NotFound();
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

                var finalStep = task.Steps
                    .OrderByDescending(step => step.StepIndex)
                    .FirstOrDefault(step => string.Equals(step.ToolCode, "finalize_artifacts", StringComparison.OrdinalIgnoreCase));
                if (finalStep?.Status == AgentStepStatus.WaitingApproval)
                {
                    finalStep.Approve();
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
