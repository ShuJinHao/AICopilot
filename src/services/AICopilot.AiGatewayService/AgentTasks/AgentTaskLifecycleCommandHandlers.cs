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
    AgentTaskDtoQueryService dtoQueryService,
    AgentApprovalDecisionCoordinator approvalDecisionCoordinator)
    : ICommandHandler<ApproveAgentTaskPlanCommand, Result<AgentTaskDto>>
{
    public async Task<Result<AgentTaskDto>> Handle(ApproveAgentTaskPlanCommand request, CancellationToken cancellationToken)
    {
        var result = await approvalDecisionCoordinator.ApprovePlanForTaskAsync(
            request.Id,
            "Plan approved.",
            cancellationToken);
        return result.IsSuccess
            ? Result.Success(await dtoQueryService.MapAsync(result.Value!, cancellationToken))
            : Result.From(result);
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
