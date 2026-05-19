using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Tools;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed record AgentStepPlanDto(
    string Title,
    string Description,
    AgentStepType StepType,
    string? ToolCode,
    bool RequiresApproval,
    string? InputJson = null);

public sealed record AgentStepDto(
    Guid Id,
    int StepIndex,
    string Title,
    string Description,
    string StepType,
    string Status,
    string? ToolCode,
    bool RequiresApproval,
    string? ErrorMessage);

public sealed record AgentTaskFailureSummaryDto(
    int? StepIndex,
    string? ToolCode,
    string ErrorCode,
    string SafeMessage,
    bool CanRetry,
    string NextAction);

public sealed record AgentTaskDto(
    Guid Id,
    string TaskCode,
    Guid SessionId,
    string Title,
    string Goal,
    string TaskType,
    string Status,
    string RiskLevel,
    Guid? ModelId,
    Guid? WorkspaceId,
    string PlanJson,
    string? FinalSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyCollection<AgentStepDto> Steps,
    string? WorkspaceCode = null,
    int PendingApprovalCount = 0,
    string? LastFailureReason = null,
    bool CanRun = false,
    bool CanRetry = false,
    bool CanSubmitFinalReview = false,
    bool CanApproveFinal = false,
    AgentTaskFailureSummaryDto? FailureSummary = null,
    Guid? ActiveRunAttemptId = null,
    int RunAttemptCount = 0,
    bool IsRunInProgress = false,
    Guid? QueuedRunId = null,
    string? RunQueueStatus = null,
    bool IsRunQueued = false);

public sealed record AgentTaskRunAttemptDto(
    Guid Id,
    Guid TaskId,
    int AttemptNo,
    string Status,
    string TriggerType,
    Guid? LeaseId,
    string? LeaseOwner,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureCode,
    string? SafeMessage);

public sealed record AgentTaskRunAttemptPageDto(
    IReadOnlyCollection<AgentTaskRunAttemptDto> Items,
    int PageIndex,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPrevious,
    bool HasNext);

public sealed record AgentTaskRunQueueItemDto(
    Guid Id,
    Guid TaskId,
    string TriggerType,
    string Status,
    Guid RequestedBy,
    Guid? RunAttemptId,
    Guid? LeaseId,
    string? LeaseOwner,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset AvailableAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureCode,
    string? SafeMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AgentTaskRunQueuePageDto(
    IReadOnlyCollection<AgentTaskRunQueueItemDto> Items,
    int PageIndex,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPrevious,
    bool HasNext);

public sealed record AgentRunQueueItemDto(
    Guid Id,
    Guid TaskId,
    string TriggerType,
    string Status,
    Guid RequestedBy,
    Guid? RunAttemptId,
    Guid? LeaseId,
    string? LeaseOwner,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset AvailableAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureCode,
    string? SafeMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AgentRunQueuePageDto(
    IReadOnlyCollection<AgentRunQueueItemDto> Items,
    int PageIndex,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPrevious,
    bool HasNext);

public sealed record AgentRunQueueSummaryDto(
    int QueuedCount,
    int LeasedCount,
    int SucceededCount,
    int FailedCount,
    int CancelledCount,
    int DeadLetterCount,
    int StaleLeasedCount,
    DateTimeOffset? OldestQueuedAt,
    long? AverageWaitMs,
    long? AverageRunMs,
    long? OldestQueuedWaitMs,
    int ActiveWorkerCount,
    int WorkspaceMismatchCount,
    DateTimeOffset GeneratedAt);

public sealed record AgentWorkerHeartbeatDto(
    Guid Id,
    string WorkerId,
    string WorkerName,
    DateTimeOffset StartedAt,
    DateTimeOffset LastSeenAt,
    bool IsActive,
    Guid? ActiveQueueItemId,
    Guid? ActiveTaskId,
    string WorkspaceRootHash,
    string Version,
    bool WorkspaceMatchesHttpApi);

public sealed record AgentWorkerStatusDto(
    string StatusCode,
    bool HasActiveWorkers,
    bool WorkspaceConsistent,
    string HttpApiWorkspaceRootHash,
    int ActiveWorkerCount,
    int QueuedCount,
    int LeasedCount,
    int StaleLeasedCount,
    DateTimeOffset? OldestQueuedAt,
    DateTimeOffset GeneratedAt,
    IReadOnlyCollection<AgentWorkerHeartbeatDto> Workers);

internal static class AgentTaskDtoMapper
{
    public static AgentTaskDto Map(
        AgentTask task,
        string? workspaceCode = null,
        int pendingApprovalCount = 0,
        AgentTaskRunQueueItem? activeQueueItem = null,
        bool canApproveFinal = false,
        bool? canSubmitFinalReview = null)
    {
        return new AgentTaskDto(
            task.Id,
            task.TaskCode,
            task.SessionId,
            task.Title,
            task.Goal,
            task.TaskType.ToString(),
            task.Status.ToString(),
            task.RiskLevel.ToString(),
            task.ModelId?.Value,
            task.WorkspaceId?.Value,
            task.PlanJson,
            task.FinalSummary,
            task.CreatedAt,
            task.UpdatedAt,
            task.CompletedAt,
            task.Steps
                .OrderBy(step => step.StepIndex)
                .Select(MapStep)
                .ToArray(),
            workspaceCode,
            pendingApprovalCount,
            ResolveLastFailureReason(task),
            task.Status is AgentTaskStatus.PlanApproved
                or AgentTaskStatus.Running
                or AgentTaskStatus.GeneratingArtifacts
                or AgentTaskStatus.WaitingToolApproval,
            task.Status is AgentTaskStatus.Failed,
            task.Status is AgentTaskStatus.WorkspaceReady && (canSubmitFinalReview ?? true),
            task.Status is AgentTaskStatus.WaitingFinalApproval && canApproveFinal,
            AgentTaskFailureSummaryResolver.Resolve(task),
            task.ActiveRunAttemptId?.Value,
            task.RunAttemptCount,
            task.IsRunInProgress(DateTimeOffset.UtcNow),
            activeQueueItem?.Id.Value,
            activeQueueItem?.Status.ToString(),
            activeQueueItem?.IsActive ?? false);
    }

    private static AgentStepDto MapStep(AgentStep step)
    {
        return new AgentStepDto(
            step.Id,
            step.StepIndex,
            step.Title,
            step.Description,
            step.StepType.ToString(),
            step.Status.ToString(),
            step.ToolCode,
            step.RequiresApproval,
            step.ErrorMessage);
    }

    private static string? ResolveLastFailureReason(AgentTask task)
    {
        if (task.Status is AgentTaskStatus.Failed or AgentTaskStatus.Rejected)
        {
            return task.FinalSummary;
        }

        return task.Steps
            .OrderByDescending(step => step.FinishedAt)
            .FirstOrDefault(step => step.Status == AgentStepStatus.Failed)
            ?.ErrorMessage;
    }
}

internal static class AgentTaskRunAttemptDtoMapper
{
    public static AgentTaskRunAttemptDto Map(AgentTaskRunAttempt attempt)
    {
        return new AgentTaskRunAttemptDto(
            attempt.Id.Value,
            attempt.TaskId.Value,
            attempt.AttemptNo,
            attempt.Status.ToString(),
            attempt.TriggerType.ToString(),
            attempt.LeaseId,
            attempt.LeaseOwner,
            attempt.LeaseExpiresAt,
            attempt.StartedAt,
            attempt.CompletedAt,
            attempt.FailureCode,
            ToolExecutionRecordSanitizer.Sanitize(attempt.SafeMessage, 2000));
    }
}

internal static class AgentTaskRunQueueItemDtoMapper
{
    public static AgentTaskRunQueueItemDto Map(AgentTaskRunQueueItem item)
    {
        return new AgentTaskRunQueueItemDto(
            item.Id.Value,
            item.TaskId.Value,
            item.TriggerType.ToString(),
            item.Status.ToString(),
            item.RequestedBy,
            item.RunAttemptId?.Value,
            item.LeaseId,
            item.LeaseOwner,
            item.LeaseExpiresAt,
            item.AvailableAt,
            item.StartedAt,
            item.CompletedAt,
            item.FailureCode,
            ToolExecutionRecordSanitizer.Sanitize(item.SafeMessage, 2000),
            item.CreatedAt,
            item.UpdatedAt);
    }

    public static AgentRunQueueItemDto MapGlobal(AgentTaskRunQueueItem item)
    {
        return new AgentRunQueueItemDto(
            item.Id.Value,
            item.TaskId.Value,
            item.TriggerType.ToString(),
            item.Status.ToString(),
            item.RequestedBy,
            item.RunAttemptId?.Value,
            item.LeaseId,
            item.LeaseOwner,
            item.LeaseExpiresAt,
            item.AvailableAt,
            item.StartedAt,
            item.CompletedAt,
            item.FailureCode,
            ToolExecutionRecordSanitizer.Sanitize(item.SafeMessage, 2000),
            item.CreatedAt,
            item.UpdatedAt);
    }
}

internal static class AgentTaskFailureSummaryResolver
{
    public static AgentTaskFailureSummaryDto? Resolve(
        AgentTask task,
        IReadOnlyCollection<ToolExecutionRecord>? records = null)
    {
        if (task.Status is not AgentTaskStatus.Failed and not AgentTaskStatus.Rejected)
        {
            return null;
        }

        var failedStep = task.Steps
            .Where(step => step.Status == AgentStepStatus.Failed)
            .OrderByDescending(step => step.FinishedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
        var failedRecord = failedStep is null
            ? records?
                .Where(record => record.Status is ToolExecutionStatus.Failed or ToolExecutionStatus.Rejected)
                .OrderByDescending(record => record.CompletedAt ?? record.StartedAt)
                .FirstOrDefault()
            : records?
                .Where(record => record.StepId == failedStep.Id &&
                                 record.Status is ToolExecutionStatus.Failed or ToolExecutionStatus.Rejected)
                .OrderByDescending(record => record.CompletedAt ?? record.StartedAt)
                .FirstOrDefault();

        var errorCode = failedRecord?.ErrorCode ??
                        (task.Status == AgentTaskStatus.Rejected ? "agent_task_rejected" : "agent_task_failed");
        var safeMessage = failedStep?.ErrorMessage ??
                          failedRecord?.ErrorMessage ??
                          task.FinalSummary ??
                          (task.Status == AgentTaskStatus.Rejected
                              ? "Agent task was rejected."
                              : "Agent task failed.");

        return new AgentTaskFailureSummaryDto(
            failedStep?.StepIndex,
            failedStep?.ToolCode ?? failedRecord?.ToolCode,
            errorCode,
            safeMessage,
            task.Status == AgentTaskStatus.Failed,
            task.Status == AgentTaskStatus.Failed ? "retry_after_fix" : "submit_new_plan");
    }
}
