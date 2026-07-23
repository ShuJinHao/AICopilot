using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using System.Text.Json;

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
    bool IsRunQueued = false,
    string? PlanSchemaVersion = null,
    string? PlanDigest = null,
    string? TopologyProfile = null,
    bool IsPlanExecutable = false,
    string PlanIntegrityStatus = "Invalid");

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

public sealed record AgentRuntimeNodeDto(
    string NodeId,
    string Label,
    string Kind,
    string Status,
    bool IsRequired,
    int DependencyCount,
    string? JoinPolicy,
    int AttemptNo,
    int MaxAttempts,
    int RetryCount,
    int TimeoutSeconds,
    long? DurationMs,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureCode,
    string? SafeMessage);

public sealed record AgentRuntimeEvidenceQualityDto(
    int? RowCount,
    bool IsTruncated,
    string Freshness,
    double? MissingRate,
    double? Confidence,
    IReadOnlyCollection<string> Flags);

public sealed record AgentRuntimeEvidenceDto(
    string NodeId,
    string NodeLabel,
    string EvidenceKind,
    string TruthClass,
    string TruthLabel,
    string SourceLabel,
    string SourceMode,
    bool IsSimulation,
    DateTimeOffset? AsOfUtc,
    DateTimeOffset? TimeRangeStartUtc,
    DateTimeOffset? TimeRangeEndUtc,
    AgentRuntimeEvidenceQualityDto Quality,
    string SafeSummary,
    IReadOnlyCollection<string> Findings,
    IReadOnlyDictionary<string, decimal> TypedMetrics,
    int CitationCount);

public sealed record AgentRuntimeMetricDto(
    string Code,
    string Label,
    decimal? Value,
    string Unit,
    string Status,
    string Source);

public sealed record AgentTaskRuntimeSnapshotDto(
    Guid TaskId,
    Guid? RunAttemptId,
    string Status,
    DateTimeOffset GeneratedAt,
    string? EvidenceSetDigest,
    IReadOnlyCollection<AgentRuntimeNodeDto> Nodes,
    IReadOnlyCollection<AgentRuntimeEvidenceDto> Evidence,
    IReadOnlyCollection<AgentRuntimeMetricDto> Metrics);

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
        var planMetadata = AgentTaskPlanMetadataResolver.Resolve(task);
        var hasExecutablePlan =
            planMetadata.IntegrityStatus == AgentTaskPlanMetadataResolver.ValidV2 &&
            planMetadata.IsExecutable;
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
            ToolExecutionRecordSanitizer.Sanitize(task.FinalSummary, 4_000),
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
            hasExecutablePlan &&
            task.Status is (AgentTaskStatus.PlanApproved
                or AgentTaskStatus.Running
                or AgentTaskStatus.GeneratingArtifacts
                or AgentTaskStatus.WaitingToolApproval),
            hasExecutablePlan && task.Status is AgentTaskStatus.Failed,
            hasExecutablePlan &&
            task.Status is AgentTaskStatus.WorkspaceReady && (canSubmitFinalReview ?? true),
            hasExecutablePlan && task.Status is AgentTaskStatus.WaitingFinalApproval && canApproveFinal,
            AgentTaskFailureSummaryResolver.Resolve(task),
            task.ActiveRunAttemptId?.Value,
            task.RunAttemptCount,
            task.IsRunInProgress(DateTimeOffset.UtcNow),
            activeQueueItem?.Id.Value,
            activeQueueItem?.Status.ToString(),
            activeQueueItem?.IsActive ?? false,
            planMetadata.SchemaVersion,
            planMetadata.PlanDigest,
            planMetadata.TopologyProfile,
            planMetadata.IsExecutable,
            planMetadata.IntegrityStatus);
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
            ToolExecutionRecordSanitizer.Sanitize(step.ErrorMessage, 2_000));
    }

    private static string? ResolveLastFailureReason(AgentTask task)
    {
        if (task.Status is AgentTaskStatus.Failed or AgentTaskStatus.Rejected)
        {
            return ToolExecutionRecordSanitizer.Sanitize(task.FinalSummary, 2_000);
        }

        return task.Steps
            .OrderByDescending(step => step.FinishedAt)
            .FirstOrDefault(step => step.Status == AgentStepStatus.Failed)
            is { ErrorMessage: { } errorMessage }
                ? ToolExecutionRecordSanitizer.Sanitize(errorMessage, 2_000)
                : null;
    }
}

internal sealed record AgentTaskPlanDtoMetadata(
    string? SchemaVersion,
    string? PlanDigest,
    string? TopologyProfile,
    bool IsExecutable,
    string IntegrityStatus);

internal static class AgentTaskPlanMetadataResolver
{
    public const string ValidV2 = "ValidV2";
    public const string LegacyCompletedReadOnly = "LegacyCompletedReadOnly";
    public const string Invalid = "Invalid";

    public static AgentTaskPlanDtoMetadata Resolve(AgentTask task)
    {
        var validation = new AgentPlanCanonicalizer().ValidatePersisted(task.PlanJson);
        if (validation.IsSuccess)
        {
            var metadata = validation.Value!;
            return new AgentTaskPlanDtoMetadata(
                metadata.SchemaVersion,
                metadata.PlanDigest,
                metadata.TopologyProfile,
                metadata.IsExecutable,
                ValidV2);
        }

        if (task.Status == AgentTaskStatus.Completed &&
            task.CompletedAt is not null &&
            IsLegacyV1(task.PlanJson))
        {
            return new AgentTaskPlanDtoMetadata(
                AgentPlanContractVersions.LegacyV1,
                null,
                null,
                false,
                LegacyCompletedReadOnly);
        }

        return new AgentTaskPlanDtoMetadata(null, null, null, false, Invalid);
    }

    internal static bool IsLegacyV1(string planJson)
    {
        try
        {
            using var document = JsonDocument.Parse(planJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (root.TryGetProperty("schemaVersion", out var schemaVersion) &&
                schemaVersion.ValueKind == JsonValueKind.String)
            {
                return string.Equals(
                    schemaVersion.GetString(),
                    AgentPlanContractVersions.LegacyV1,
                    StringComparison.Ordinal);
            }

            return root.TryGetProperty("version", out var version) &&
                   version.ValueKind == JsonValueKind.Number &&
                   version.TryGetInt32(out var value) &&
                   value == 1;
        }
        catch (JsonException)
        {
            return false;
        }
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
        var planMetadata = AgentTaskPlanMetadataResolver.Resolve(task);
        var canRetry = task.Status == AgentTaskStatus.Failed &&
                       planMetadata.IntegrityStatus == AgentTaskPlanMetadataResolver.ValidV2 &&
                       planMetadata.IsExecutable;

        return new AgentTaskFailureSummaryDto(
            failedStep?.StepIndex,
            failedStep?.ToolCode ?? failedRecord?.ToolCode,
            errorCode,
            ToolExecutionRecordSanitizer.Sanitize(safeMessage, 2_000) ?? "Agent task failed safely.",
            canRetry,
            canRetry ? "retry_after_fix" : "submit_new_plan");
    }
}
