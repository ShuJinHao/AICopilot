using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.AiGatewayService.Sessions;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class ApproveAgentTaskPlanCommandHandler(
    IRepository<AgentTask> repository,
    IRepository<ApprovalRequest> approvalRepository,
    AgentTaskDtoQueryService dtoQueryService,
    AgentAuditRecorder auditRecorder,
    ICurrentUser currentUser,
    AgentPlanDraftConfirmationService planDraftConfirmationService,
    MessageTimelineProjectionWriter? timelineProjectionWriter = null)
    : ICommandHandler<ApproveAgentTaskPlanCommand, Result<AgentTaskDto>>
{
    public async Task<Result<AgentTaskDto>> Handle(ApproveAgentTaskPlanCommand request, CancellationToken cancellationToken)
    {
        var taskResult = await AgentTaskCommandLoader.LoadTaskAsync(repository, currentUser, request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
        var userId = currentUser.Id!.Value;
        var now = DateTimeOffset.UtcNow;
        var approval = await approvalRepository.FirstOrDefaultAsync(
            new PendingApprovalRequestByTaskAndTargetSpec(
                task.Id,
                AgentApprovalType.Plan,
                task.Id.Value.ToString()),
            cancellationToken);
        if (approval is null && task.Status is AgentTaskStatus.Draft or AgentTaskStatus.WaitingPlanApproval)
        {
            approval = new ApprovalRequest(
                task.Id,
                AgentApprovalType.Plan,
                task.Id.Value.ToString(),
                task.UserId,
                now);
            approvalRepository.Add(approval);
        }

        if (task.Status is AgentTaskStatus.Draft or AgentTaskStatus.WaitingPlanApproval)
        {
            var confirmation = await planDraftConfirmationService.ConfirmAsync(task, now, cancellationToken);
            if (!confirmation.IsSuccess)
            {
                return Result.From(confirmation);
            }

            task.ApprovePlan(now);
        }

        if (approval is not null)
        {
            approval.Approve(userId, "Plan approved.", now);
            approvalRepository.Update(approval);
            if (timelineProjectionWriter is not null)
            {
                await timelineProjectionWriter.StageApprovalDecidedAsync(task, approval, cancellationToken);
            }
        }

        repository.Update(task);
        if (approval is not null)
        {
            await auditRecorder.RecordApprovalDecisionAsync(
                approval,
                task,
                AuditResults.Succeeded,
                "Agent task plan approved.",
                cancellationToken);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(await dtoQueryService.MapAsync(task, cancellationToken));
    }
}

public sealed class RunAgentTaskCommandHandler(
    IRepository<AgentTask> repository,
    AgentTaskDtoQueryService dtoQueryService,
    AgentTaskLifecycleCoordinator lifecycleCoordinator,
    ICurrentUser currentUser)
    : ICommandHandler<RunAgentTaskCommand, Result<AgentTaskDto>>
{
    public async Task<Result<AgentTaskDto>> Handle(RunAgentTaskCommand request, CancellationToken cancellationToken)
    {
        var taskResult = await AgentTaskCommandLoader.LoadTaskAsync(repository, currentUser, request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
        var queued = await lifecycleCoordinator.QueueRunAsync(task, currentUser.Id!.Value, cancellationToken);
        return queued.IsSuccess
            ? Result.Success(await dtoQueryService.MapAsync(task, cancellationToken))
            : Result.From(queued);
    }
}

public sealed class RetryAgentTaskCommandHandler(
    IRepository<AgentTask> repository,
    AgentTaskDtoQueryService dtoQueryService,
    AgentTaskLifecycleCoordinator lifecycleCoordinator,
    ICurrentUser currentUser)
    : ICommandHandler<RetryAgentTaskCommand, Result<AgentTaskDto>>
{
    public async Task<Result<AgentTaskDto>> Handle(RetryAgentTaskCommand request, CancellationToken cancellationToken)
    {
        var taskResult = await AgentTaskCommandLoader.LoadTaskAsync(repository, currentUser, request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
        var queued = await lifecycleCoordinator.RetryAsync(task, currentUser.Id!.Value, cancellationToken);
        if (!queued.IsSuccess)
        {
            return Result.From(queued);
        }

        return Result.Success(await dtoQueryService.MapAsync(task, cancellationToken));
    }
}

public sealed class CancelAgentTaskCommandHandler(
    IRepository<AgentTask> repository,
    AgentTaskDtoQueryService dtoQueryService,
    AgentTaskLifecycleCoordinator lifecycleCoordinator,
    ICurrentUser currentUser)
    : ICommandHandler<CancelAgentTaskCommand, Result<AgentTaskDto>>
{
    public async Task<Result<AgentTaskDto>> Handle(CancelAgentTaskCommand request, CancellationToken cancellationToken)
    {
        var taskResult = await AgentTaskCommandLoader.LoadTaskAsync(repository, currentUser, request.Id, cancellationToken);
        if (!taskResult.IsSuccess)
        {
            return Result.From(taskResult);
        }

        var task = taskResult.Value!;
        if (IsTerminal(task.Status))
        {
            return Result.Success(await dtoQueryService.MapAsync(task, cancellationToken));
        }

        var cancelResult = await lifecycleCoordinator.CancelAsync(task, cancellationToken);
        if (!cancelResult.IsSuccess)
        {
            return Result.From(cancelResult);
        }

        return Result.Success(await dtoQueryService.MapAsync(task, cancellationToken));
    }

    private static bool IsTerminal(AgentTaskStatus status)
    {
        return status is AgentTaskStatus.Completed
            or AgentTaskStatus.Finalized
            or AgentTaskStatus.Failed
            or AgentTaskStatus.Rejected
            or AgentTaskStatus.Cancelled;
    }
}
