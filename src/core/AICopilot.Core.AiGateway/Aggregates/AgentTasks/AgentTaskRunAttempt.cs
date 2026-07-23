using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
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

    public long TaskFencingToken { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? FailureCode { get; private set; }

    public string? SafeMessage { get; private set; }

    public bool IsBudgetInitialized { get; private set; }

    public string BudgetPolicyVersion { get; private set; } = string.Empty;

    public int BudgetMaxNodes { get; private set; }

    public int BudgetMaxToolCalls { get; private set; }

    public int BudgetMaxModelCalls { get; private set; }

    public int BudgetMaxInputTokens { get; private set; }

    public int BudgetMaxOutputTokens { get; private set; }

    public int BudgetMaxElapsedSeconds { get; private set; }

    public decimal BudgetMaxCostAmount { get; private set; }

    public string BudgetCostCurrency { get; private set; } = "CNY";

    public int BudgetMaxRetries { get; private set; }

    public int BudgetMaxArtifactCount { get; private set; }

    public long BudgetMaxArtifactBytes { get; private set; }

    public int BudgetReservedToolCalls { get; private set; }

    public int BudgetReservedModelCalls { get; private set; }

    public int BudgetReservedInputTokens { get; private set; }

    public int BudgetReservedOutputTokens { get; private set; }

    public long BudgetReservedElapsedMilliseconds { get; private set; }

    private decimal _budgetReservedCostAmount;
    public decimal BudgetReservedCostAmount => _budgetReservedCostAmount;

    public int BudgetReservedRetries { get; private set; }

    public int BudgetReservedArtifactCount { get; private set; }

    public long BudgetReservedArtifactBytes { get; private set; }

    private int _budgetConsumedToolCalls;
    public int BudgetConsumedToolCalls => _budgetConsumedToolCalls;
    private int _budgetConsumedModelCalls;
    public int BudgetConsumedModelCalls => _budgetConsumedModelCalls;
    private int _budgetConsumedInputTokens;
    public int BudgetConsumedInputTokens => _budgetConsumedInputTokens;
    private int _budgetConsumedOutputTokens;
    public int BudgetConsumedOutputTokens => _budgetConsumedOutputTokens;
    private long _budgetConsumedElapsedMilliseconds;
    public long BudgetConsumedElapsedMilliseconds => _budgetConsumedElapsedMilliseconds;
    private decimal _budgetConsumedCostAmount;
    public decimal BudgetConsumedCostAmount => _budgetConsumedCostAmount;
    private int _budgetConsumedRetries;
    public int BudgetConsumedRetries => _budgetConsumedRetries;
    private int _budgetConsumedArtifactCount;
    public int BudgetConsumedArtifactCount => _budgetConsumedArtifactCount;
    private long _budgetConsumedArtifactBytes;
    public long BudgetConsumedArtifactBytes => _budgetConsumedArtifactBytes;

    public bool IsTerminal => Status is AgentTaskRunAttemptStatus.Succeeded
        or AgentTaskRunAttemptStatus.Failed
        or AgentTaskRunAttemptStatus.Cancelled;

    public bool HasActiveLease(DateTimeOffset nowUtc)
    {
        return LeaseExpiresAt.HasValue && LeaseExpiresAt.Value > nowUtc;
    }

    public void InitializeBudget(AgentRunBudgetLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var policyVersion = NormalizeOptional(limits.PolicyVersion, 120);
        var currency = NormalizeOptional(limits.CostCurrency, 8);
        if (policyVersion is null || currency is null ||
            limits.MaxNodes <= 0 || limits.MaxToolCalls < 0 || limits.MaxModelCalls < 0 ||
            limits.MaxInputTokens < 0 || limits.MaxOutputTokens < 0 || limits.MaxElapsedSeconds <= 0 ||
            limits.MaxCostAmount < 0 || limits.MaxRetries < 0 || limits.MaxArtifactCount < 0 ||
            limits.MaxArtifactBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "Agent run budget limits are invalid.");
        }

        if (IsBudgetInitialized)
        {
            if (!MatchesBudget(limits, policyVersion, currency))
            {
                throw new InvalidOperationException("RunAttempt budget does not match the immutable Plan v2 budget.");
            }

            return;
        }

        BudgetPolicyVersion = policyVersion;
        BudgetMaxNodes = limits.MaxNodes;
        BudgetMaxToolCalls = limits.MaxToolCalls;
        BudgetMaxModelCalls = limits.MaxModelCalls;
        BudgetMaxInputTokens = limits.MaxInputTokens;
        BudgetMaxOutputTokens = limits.MaxOutputTokens;
        BudgetMaxElapsedSeconds = limits.MaxElapsedSeconds;
        BudgetMaxCostAmount = limits.MaxCostAmount;
        BudgetCostCurrency = currency;
        BudgetMaxRetries = limits.MaxRetries;
        BudgetMaxArtifactCount = limits.MaxArtifactCount;
        BudgetMaxArtifactBytes = limits.MaxArtifactBytes;
        IsBudgetInitialized = true;
    }

    public AgentRunBudgetReservationResult TryReserveBudget(
        AgentRunBudgetCharge reservation,
        DateTimeOffset nowUtc)
    {
        if (!IsBudgetInitialized)
        {
            return AgentRunBudgetReservationResult.BudgetNotInitialized;
        }

        if (!reservation.IsNonNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(reservation));
        }

        if (nowUtc < StartedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(nowUtc));
        }

        var result = FirstExceededDimension(reservation);
        if (result != AgentRunBudgetReservationResult.Reserved)
        {
            return result;
        }

        SetReservedBudget(checked(ReservedBudget + reservation));
        return AgentRunBudgetReservationResult.Reserved;
    }

    public bool TrySettleBudget(
        AgentRunBudgetCharge reservation,
        AgentRunBudgetCharge actual,
        bool conservativelyConsumed)
    {
        if (!IsBudgetInitialized ||
            !reservation.IsNonNegative ||
            !actual.IsWithin(reservation) ||
            !HasReserved(reservation))
        {
            return false;
        }

        SetReservedBudget(checked(ReservedBudget - reservation));
        var charge = conservativelyConsumed ? reservation : actual;
        SetConsumedBudget(checked(ConsumedBudget + charge));
        return true;
    }

    public bool TryReleaseBudget(AgentRunBudgetCharge reservation, bool consumeRetry)
    {
        var actual = AgentRunBudgetCharge.Zero with
        {
            RetryCount = consumeRetry ? reservation.RetryCount : 0
        };
        return TrySettleBudget(reservation, actual, conservativelyConsumed: false);
    }

    public void BindTaskFencingToken(long taskFencingToken)
    {
        if (taskFencingToken <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(taskFencingToken));
        }

        TaskFencingToken = taskFencingToken;
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

    public void RequireReconciliation(DateTimeOffset nowUtc, string safeMessage)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException("Terminal agent run attempts cannot require reconciliation.");
        }

        Status = AgentTaskRunAttemptStatus.ReconciliationRequired;
        SafeMessage = NormalizeOptional(safeMessage, 2000);
        ReleaseLease();
    }

    public void ResumeFromReconciliationForReclaim(DateTimeOffset nowUtc)
    {
        if (Status != AgentTaskRunAttemptStatus.ReconciliationRequired)
        {
            throw new InvalidOperationException("Only a reconciliation-blocked run attempt can resume.");
        }

        Status = AgentTaskRunAttemptStatus.Created;
        FailureCode = null;
        SafeMessage = null;
        CompletedAt = null;
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

    private AgentRunBudgetReservationResult FirstExceededDimension(AgentRunBudgetCharge reservation)
    {
        if (WouldExceed(BudgetConsumedToolCalls, BudgetReservedToolCalls, reservation.ToolCalls, BudgetMaxToolCalls))
        {
            return AgentRunBudgetReservationResult.ToolCallsExceeded;
        }

        if (WouldExceed(BudgetConsumedModelCalls, BudgetReservedModelCalls, reservation.ModelCalls, BudgetMaxModelCalls))
        {
            return AgentRunBudgetReservationResult.ModelCallsExceeded;
        }

        if (WouldExceed(BudgetConsumedInputTokens, BudgetReservedInputTokens, reservation.InputTokens, BudgetMaxInputTokens))
        {
            return AgentRunBudgetReservationResult.InputTokensExceeded;
        }

        if (WouldExceed(BudgetConsumedOutputTokens, BudgetReservedOutputTokens, reservation.OutputTokens, BudgetMaxOutputTokens))
        {
            return AgentRunBudgetReservationResult.OutputTokensExceeded;
        }

        if (WouldExceed(
                BudgetConsumedElapsedMilliseconds,
                BudgetReservedElapsedMilliseconds,
                reservation.ElapsedMilliseconds,
                checked((long)BudgetMaxElapsedSeconds * 1000L)))
        {
            return AgentRunBudgetReservationResult.ElapsedUsageExceeded;
        }

        if (BudgetConsumedCostAmount + BudgetReservedCostAmount + reservation.CostAmount > BudgetMaxCostAmount)
        {
            return AgentRunBudgetReservationResult.CostExceeded;
        }

        if (WouldExceed(BudgetConsumedRetries, BudgetReservedRetries, reservation.RetryCount, BudgetMaxRetries))
        {
            return AgentRunBudgetReservationResult.RetriesExceeded;
        }

        if (WouldExceed(BudgetConsumedArtifactCount, BudgetReservedArtifactCount, reservation.ArtifactCount, BudgetMaxArtifactCount))
        {
            return AgentRunBudgetReservationResult.ArtifactCountExceeded;
        }

        return WouldExceed(
                BudgetConsumedArtifactBytes,
                BudgetReservedArtifactBytes,
                reservation.ArtifactBytes,
                BudgetMaxArtifactBytes)
            ? AgentRunBudgetReservationResult.ArtifactBytesExceeded
            : AgentRunBudgetReservationResult.Reserved;
    }

    private bool HasReserved(AgentRunBudgetCharge reservation)
    {
        return reservation.IsWithin(ReservedBudget);
    }

    private AgentRunBudgetCharge ReservedBudget => new(BudgetReservedToolCalls, BudgetReservedModelCalls, BudgetReservedInputTokens, BudgetReservedOutputTokens, BudgetReservedElapsedMilliseconds, BudgetReservedCostAmount, BudgetReservedRetries, BudgetReservedArtifactCount, BudgetReservedArtifactBytes);

    private AgentRunBudgetCharge ConsumedBudget => new(BudgetConsumedToolCalls, BudgetConsumedModelCalls, BudgetConsumedInputTokens, BudgetConsumedOutputTokens, BudgetConsumedElapsedMilliseconds, BudgetConsumedCostAmount, BudgetConsumedRetries, BudgetConsumedArtifactCount, BudgetConsumedArtifactBytes);

    private void SetReservedBudget(AgentRunBudgetCharge value)
    {
        (BudgetReservedToolCalls, BudgetReservedModelCalls, BudgetReservedInputTokens, BudgetReservedOutputTokens, BudgetReservedElapsedMilliseconds, _budgetReservedCostAmount, BudgetReservedRetries, BudgetReservedArtifactCount, BudgetReservedArtifactBytes) =
            (value.ToolCalls, value.ModelCalls, value.InputTokens, value.OutputTokens, value.ElapsedMilliseconds, value.CostAmount, value.RetryCount, value.ArtifactCount, value.ArtifactBytes);
    }

    private void SetConsumedBudget(AgentRunBudgetCharge value)
    {
        _budgetConsumedToolCalls = value.ToolCalls;
        _budgetConsumedModelCalls = value.ModelCalls;
        _budgetConsumedInputTokens = value.InputTokens;
        _budgetConsumedOutputTokens = value.OutputTokens;
        _budgetConsumedElapsedMilliseconds = value.ElapsedMilliseconds;
        _budgetConsumedCostAmount = value.CostAmount;
        _budgetConsumedRetries = value.RetryCount;
        _budgetConsumedArtifactCount = value.ArtifactCount;
        _budgetConsumedArtifactBytes = value.ArtifactBytes;
    }

    private bool MatchesBudget(AgentRunBudgetLimits limits, string policyVersion, string currency)
    {
        var persisted = new AgentRunBudgetLimits(BudgetPolicyVersion, BudgetMaxNodes, BudgetMaxToolCalls, BudgetMaxModelCalls, BudgetMaxInputTokens, BudgetMaxOutputTokens, BudgetMaxElapsedSeconds, BudgetMaxCostAmount, BudgetCostCurrency, BudgetMaxRetries, BudgetMaxArtifactCount, BudgetMaxArtifactBytes);
        return persisted == limits with { PolicyVersion = policyVersion, CostCurrency = currency };
    }

    private static bool WouldExceed(int consumed, int reserved, int requested, int maximum)
    {
        return (long)consumed + reserved + requested > maximum;
    }

    private static bool WouldExceed(long consumed, long reserved, long requested, long maximum)
    {
        return consumed > maximum - reserved - requested;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is { Length: > 0 } && normalized.Length > maxLength
            ? normalized[..maxLength]
            : normalized;
    }
}
