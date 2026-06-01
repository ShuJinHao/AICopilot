using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentTaskRunAttemptCoordinator(
    IRepository<AgentTask> taskRepository,
    IRepository<AgentTaskRunAttempt> runAttemptRepository,
    IOptions<AgentRunQueueOptions>? runQueueOptions = null)
{
    private const string RunLeaseOwner = "agent-runtime-sync";
    private TimeSpan RunLeaseDuration => runQueueOptions?.Value.LeaseDuration ?? new AgentRunQueueOptions().LeaseDuration;

    public async Task<Result<AgentTaskRunAttempt>> BeginOrResumeAttemptAsync(
    AgentTask task,
    AgentTaskRunTriggerType triggerType,
    CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (task.IsRunInProgress(now))
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentTaskRunInProgress,
                "Agent task already has an active run lease."));
        }

        if (task.Status is AgentTaskStatus.WorkspaceReady or AgentTaskStatus.WaitingFinalApproval)
        {
            return Result.Invalid("Agent task is waiting for final output approval and cannot be run again.");
        }

        if (task.ActiveRunAttemptId is not null)
        {
            var activeAttempt = await runAttemptRepository.FirstOrDefaultAsync(
                new AgentTaskRunAttemptByIdSpec(task.ActiveRunAttemptId.Value),
                cancellationToken);
            if (activeAttempt is not null && !activeAttempt.IsTerminal)
            {
                if (task.Status == AgentTaskStatus.WaitingToolApproval ||
                    activeAttempt.Status == AgentTaskRunAttemptStatus.WaitingApproval ||
                    task.Steps.Any(step => step.Status == AgentStepStatus.Approved))
                {
                    return await AcquireAttemptLeaseAsync(task, activeAttempt, cancellationToken);
                }

                var message = "Previous agent task run lease expired. Retry the task before continuing.";
                activeAttempt.MarkFailed(AppProblemCodes.AgentTaskRunLeaseExpired, message, now);
                task.Fail(message, now);
                task.ReleaseRunLease(now, clearActiveAttempt: true);
                runAttemptRepository.Update(activeAttempt);
                taskRepository.Update(task);
                await taskRepository.SaveChangesAsync(cancellationToken);
                return Result.Failure(new ApiProblemDescriptor(AppProblemCodes.AgentTaskRunLeaseExpired, message));
            }
        }

        if (task.Status is not AgentTaskStatus.PlanApproved and not AgentTaskStatus.WaitingToolApproval)
        {
            return Result.Invalid("Only approved or waiting-approval agent tasks can be executed.");
        }

        var attempt = new AgentTaskRunAttempt(
            task.Id,
            task.RunAttemptCount + 1,
            triggerType,
            RunLeaseOwner,
            now,
            RunLeaseDuration);
        runAttemptRepository.Add(attempt);
        task.BeginRunAttempt(
            attempt.Id,
            attempt.AttemptNo,
            attempt.LeaseId!.Value,
            attempt.LeaseOwner ?? RunLeaseOwner,
            attempt.LeaseExpiresAt!.Value,
            now);
        taskRepository.Update(task);
        await taskRepository.SaveChangesAsync(cancellationToken);
        return Result.Success(attempt);
    }

    private async Task<Result<AgentTaskRunAttempt>> AcquireAttemptLeaseAsync(
        AgentTask task,
        AgentTaskRunAttempt attempt,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        attempt.AcquireLease(Guid.NewGuid(), RunLeaseOwner, now, RunLeaseDuration);
        task.AcquireRunLease(
            attempt.LeaseId!.Value,
            attempt.LeaseOwner ?? RunLeaseOwner,
            attempt.LeaseExpiresAt!.Value,
            now);
        runAttemptRepository.Update(attempt);
        taskRepository.Update(task);
        await taskRepository.SaveChangesAsync(cancellationToken);
        return Result.Success(attempt);
    }

    public async Task RefreshRunLeaseAsync(
        AgentTask task,
        AgentTaskRunAttempt attempt,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        attempt.RefreshLease(now, RunLeaseDuration);
        task.AcquireRunLease(
            attempt.LeaseId!.Value,
            attempt.LeaseOwner ?? RunLeaseOwner,
            attempt.LeaseExpiresAt!.Value,
            now);
        runAttemptRepository.Update(attempt);
        taskRepository.Update(task);
        await taskRepository.SaveChangesAsync(cancellationToken);
    }

}
