using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.AgentTasks;

public sealed class AgentTaskRunAttempt : BaseEntity<AgentTaskRunAttemptId>
{
    private AgentTaskRunAttempt()
    {
    }

    public AgentTaskRunAttempt(
        AgentTaskId taskId,
        int attemptNo,
        AgentTaskRunTriggerType triggerType,
        string leaseOwner,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration)
    {
        if (attemptNo <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNo), "Agent run attempt number must be greater than zero.");
        }

        Id = AgentTaskRunAttemptId.New();
        TaskId = taskId;
        AttemptNo = attemptNo;
        TriggerType = triggerType;
        Status = AgentTaskRunAttemptStatus.Running;
        StartedAt = nowUtc;
        AcquireLease(Guid.NewGuid(), leaseOwner, nowUtc, leaseDuration);
    }

    public AgentTaskId TaskId { get; private set; }

    public int AttemptNo { get; private set; }

    public AgentTaskRunAttemptStatus Status { get; private set; }

    public AgentTaskRunTriggerType TriggerType { get; private set; }

    public Guid? LeaseId { get; private set; }

    public string? LeaseOwner { get; private set; }

    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? FailureCode { get; private set; }

    public string? SafeMessage { get; private set; }

    public bool IsTerminal => Status is AgentTaskRunAttemptStatus.Succeeded
        or AgentTaskRunAttemptStatus.Failed
        or AgentTaskRunAttemptStatus.Cancelled;

    public bool HasActiveLease(DateTimeOffset nowUtc)
    {
        return LeaseExpiresAt.HasValue && LeaseExpiresAt.Value > nowUtc;
    }

    public void AcquireLease(
        Guid leaseId,
        string leaseOwner,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException("Terminal agent run attempts cannot acquire a lease.");
        }

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Agent run lease duration must be positive.");
        }

        Status = AgentTaskRunAttemptStatus.Running;
        LeaseId = leaseId == Guid.Empty ? Guid.NewGuid() : leaseId;
        LeaseOwner = NormalizeOptional(leaseOwner, 120) ?? "agent-runtime";
        LeaseExpiresAt = nowUtc.Add(leaseDuration);
    }

    public void RefreshLease(DateTimeOffset nowUtc, TimeSpan leaseDuration)
    {
        if (!LeaseId.HasValue)
        {
            AcquireLease(Guid.NewGuid(), LeaseOwner ?? "agent-runtime", nowUtc, leaseDuration);
            return;
        }

        LeaseExpiresAt = nowUtc.Add(leaseDuration);
    }

    public void WaitForApproval(DateTimeOffset nowUtc, string safeMessage)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException("Terminal agent run attempts cannot wait for approval.");
        }

        Status = AgentTaskRunAttemptStatus.WaitingApproval;
        SafeMessage = NormalizeOptional(safeMessage, 2000);
        ReleaseLease();
    }

    public void MarkSucceeded(DateTimeOffset nowUtc, string? safeMessage = null)
    {
        Complete(AgentTaskRunAttemptStatus.Succeeded, null, safeMessage, nowUtc);
    }

    public void MarkFailed(string failureCode, string safeMessage, DateTimeOffset nowUtc)
    {
        Complete(AgentTaskRunAttemptStatus.Failed, failureCode, safeMessage, nowUtc);
    }

    public void Cancel(DateTimeOffset nowUtc, string safeMessage)
    {
        Complete(AgentTaskRunAttemptStatus.Cancelled, "agent_task_cancelled", safeMessage, nowUtc);
    }

    private void Complete(
        AgentTaskRunAttemptStatus status,
        string? failureCode,
        string? safeMessage,
        DateTimeOffset nowUtc)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException("Terminal agent run attempts cannot be completed again.");
        }

        Status = status;
        FailureCode = NormalizeOptional(failureCode, 120);
        SafeMessage = NormalizeOptional(safeMessage, 2000);
        CompletedAt = nowUtc;
        ReleaseLease();
    }

    private void ReleaseLease()
    {
        LeaseId = null;
        LeaseOwner = null;
        LeaseExpiresAt = null;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is { Length: > 0 } && normalized.Length > maxLength
            ? normalized[..maxLength]
            : normalized;
    }
}
