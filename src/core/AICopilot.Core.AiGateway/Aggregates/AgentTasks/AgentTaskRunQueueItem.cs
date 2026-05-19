using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Result;

namespace AICopilot.Core.AiGateway.Aggregates.AgentTasks;

public sealed class AgentTaskRunQueueItem : BaseEntity<AgentTaskRunQueueItemId>, IAggregateRoot<AgentTaskRunQueueItemId>
{
    private AgentTaskRunQueueItem()
    {
    }

    public AgentTaskRunQueueItem(
        AgentTaskId taskId,
        AgentTaskRunTriggerType triggerType,
        Guid requestedBy,
        DateTimeOffset nowUtc,
        DateTimeOffset? availableAt = null)
    {
        if (requestedBy == Guid.Empty)
        {
            throw new ArgumentException("Queue requester is required.", nameof(requestedBy));
        }

        Id = AgentTaskRunQueueItemId.New();
        TaskId = taskId;
        TriggerType = triggerType;
        RequestedBy = requestedBy;
        Status = AgentTaskRunQueueStatus.Queued;
        AvailableAt = availableAt ?? nowUtc;
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public AgentTaskId TaskId { get; private set; }

    public AgentTaskRunTriggerType TriggerType { get; private set; }

    public AgentTaskRunQueueStatus Status { get; private set; }

    public Guid RequestedBy { get; private set; }

    public AgentTaskRunAttemptId? RunAttemptId { get; private set; }

    public Guid? LeaseId { get; private set; }

    public string? LeaseOwner { get; private set; }

    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public DateTimeOffset AvailableAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? FailureCode { get; private set; }

    public string? SafeMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive => Status is AgentTaskRunQueueStatus.Queued or AgentTaskRunQueueStatus.Leased;

    public bool HasActiveLease(DateTimeOffset nowUtc)
    {
        return Status == AgentTaskRunQueueStatus.Leased &&
               LeaseExpiresAt.HasValue &&
               LeaseExpiresAt.Value > nowUtc;
    }

    public bool CanBeLeased(DateTimeOffset nowUtc)
    {
        if (Status == AgentTaskRunQueueStatus.Queued)
        {
            return AvailableAt <= nowUtc;
        }

        return Status == AgentTaskRunQueueStatus.Leased &&
               StartedAt is null &&
               (!LeaseExpiresAt.HasValue || LeaseExpiresAt.Value <= nowUtc);
    }

    public bool IsExpiredStartedLease(DateTimeOffset nowUtc)
    {
        return Status == AgentTaskRunQueueStatus.Leased &&
               StartedAt is not null &&
               LeaseExpiresAt.HasValue &&
               LeaseExpiresAt.Value <= nowUtc;
    }

    public bool CanMoveToDeadLetter(DateTimeOffset nowUtc)
    {
        return Status == AgentTaskRunQueueStatus.Queued ||
               Status == AgentTaskRunQueueStatus.Failed ||
               (Status == AgentTaskRunQueueStatus.Leased &&
                LeaseExpiresAt.HasValue &&
                LeaseExpiresAt.Value <= nowUtc);
    }

    public void AcquireLease(Guid leaseId, string leaseOwner, DateTimeOffset nowUtc, TimeSpan leaseDuration)
    {
        if (!CanBeLeased(nowUtc))
        {
            throw new InvalidOperationException("Agent task run queue item is not available for leasing.");
        }

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Queue lease duration must be positive.");
        }

        Status = AgentTaskRunQueueStatus.Leased;
        LeaseId = leaseId == Guid.Empty ? Guid.NewGuid() : leaseId;
        LeaseOwner = NormalizeOptional(leaseOwner, 120) ?? "agent-run-queue";
        LeaseExpiresAt = nowUtc.Add(leaseDuration);
        FailureCode = null;
        SafeMessage = null;
        UpdatedAt = nowUtc;
    }

    public void MarkStarted(AgentTaskRunAttemptId? runAttemptId, DateTimeOffset nowUtc)
    {
        if (Status != AgentTaskRunQueueStatus.Leased)
        {
            throw new InvalidOperationException("Only leased queue items can be started.");
        }

        if (runAttemptId is not null)
        {
            RunAttemptId = runAttemptId;
        }

        StartedAt ??= nowUtc;
        UpdatedAt = nowUtc;
    }

    public void LinkRunAttempt(AgentTaskRunAttemptId runAttemptId, DateTimeOffset nowUtc)
    {
        RunAttemptId = runAttemptId;
        UpdatedAt = nowUtc;
    }

    public void MarkSucceeded(DateTimeOffset nowUtc, string? safeMessage = null)
    {
        Complete(AgentTaskRunQueueStatus.Succeeded, null, safeMessage, nowUtc);
    }

    public void MarkFailed(string failureCode, string safeMessage, DateTimeOffset nowUtc)
    {
        Complete(AgentTaskRunQueueStatus.Failed, failureCode, safeMessage, nowUtc);
    }

    public void Cancel(DateTimeOffset nowUtc, string? safeMessage = null)
    {
        Complete(AgentTaskRunQueueStatus.Cancelled, AppProblemCodes.AgentTaskCancellationRequested, safeMessage, nowUtc);
    }

    public void MarkDeadLetter(string failureCode, string safeMessage, DateTimeOffset nowUtc)
    {
        if (!CanMoveToDeadLetter(nowUtc))
        {
            throw new InvalidOperationException("Agent task run queue item cannot be moved to dead letter.");
        }

        Status = AgentTaskRunQueueStatus.DeadLetter;
        FailureCode = NormalizeOptional(failureCode, 120);
        SafeMessage = NormalizeOptional(safeMessage, 2000);
        CompletedAt = nowUtc;
        LeaseId = null;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        UpdatedAt = nowUtc;
    }

    private void Complete(
        AgentTaskRunQueueStatus status,
        string? failureCode,
        string? safeMessage,
        DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        Status = status;
        FailureCode = NormalizeOptional(failureCode, 120);
        SafeMessage = NormalizeOptional(safeMessage, 2000);
        CompletedAt = nowUtc;
        LeaseId = null;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        UpdatedAt = nowUtc;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is { Length: > 0 } && normalized.Length > maxLength
            ? normalized[..maxLength]
            : normalized;
    }
}
