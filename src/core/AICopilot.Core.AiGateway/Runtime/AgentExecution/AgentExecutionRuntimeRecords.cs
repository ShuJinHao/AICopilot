using System.Text;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Runtime.AgentExecution;

public enum AgentNodeRunStatus
{
    Pending = 0,
    Runnable = 1,
    WaitingApproval = 2,
    Claimed = 3,
    Running = 4,
    Succeeded = 5,
    Failed = 6,
    Cancelled = 7,
    OutcomeUnknown = 8
}

public enum AgentNodeSideEffectClass
{
    ReadOnly = 0,
    DeterministicInternal = 1,
    ArtifactWrite = 2,
    ExternalIdempotent = 3,
    ExternalOutcomeUnknown = 4
}

public enum AgentEvidenceKind
{
    DataQuery = 0,
    RagCitation = 1,
    UploadedFile = 2,
    DerivedMetric = 3,
    ModelPrediction = 4,
    LlmInference = 5,
    PolicyDecision = 6,
    ArtifactReference = 7
}

public enum AgentEvidenceTruthClass
{
    ObservedFact = 0,
    DerivedFact = 1,
    ModelPrediction = 2,
    LlmInference = 3,
    Recommendation = 4
}

public enum AgentEvidenceStorageMode
{
    InlineCanonicalJson = 0,
    ArtifactReference = 1
}

public enum AgentOutcomeReconciliationResolution
{
    ConfirmedSucceeded = 0,
    ConfirmedNotOccurred = 1,
    ConfirmedCancelled = 2,
    StillUnknown = 3,
    ConflictingEvidence = 4,
    ManualAbandonedAsFailed = 5,
    ManualAbandonedAsCancelled = 6
}

public enum ModelQuotaReservationStatus
{
    Active = 0,
    Settled = 1,
    Released = 2,
    ReconciliationRequired = 3,
    Expired = 4
}

public enum ArtifactFileSetOperationStatus
{
    Prepared = 0,
    Published = 1,
    DatabaseCommitted = 2,
    Completed = 3,
    RollbackPending = 4,
    ReconciliationRequired = 5,
    Failed = 6
}

public enum AgentBudgetReservationStatus
{
    None = 0,
    Active = 1,
    Settled = 2,
    Released = 3,
    ConservativelyConsumed = 4
}

public enum AgentRunBudgetReservationResult
{
    Reserved = 0,
    BudgetNotInitialized = 1,
    WorkflowElapsedExceeded = 2,
    ToolCallsExceeded = 3,
    ModelCallsExceeded = 4,
    InputTokensExceeded = 5,
    OutputTokensExceeded = 6,
    ElapsedUsageExceeded = 7,
    CostExceeded = 8,
    RetriesExceeded = 9,
    ArtifactCountExceeded = 10,
    ArtifactBytesExceeded = 11
}

public sealed record AgentRunBudgetLimits(
    string PolicyVersion,
    int MaxNodes,
    int MaxToolCalls,
    int MaxModelCalls,
    int MaxInputTokens,
    int MaxOutputTokens,
    int MaxElapsedSeconds,
    decimal MaxCostAmount,
    string CostCurrency,
    int MaxRetries,
    int MaxArtifactCount,
    long MaxArtifactBytes);

public sealed record AgentNodeBudgetLimits(
    int MaxToolCalls,
    int MaxModelCalls,
    int MaxInputTokens,
    int MaxOutputTokens,
    decimal MaxCostAmount,
    int MaxArtifactCount,
    long MaxArtifactBytes);

public readonly record struct AgentRunBudgetCharge(
    int ToolCalls,
    int ModelCalls,
    int InputTokens,
    int OutputTokens,
    long ElapsedMilliseconds,
    decimal CostAmount,
    int RetryCount,
    int ArtifactCount,
    long ArtifactBytes)
{
    public static AgentRunBudgetCharge Zero => new(0, 0, 0, 0, 0, 0m, 0, 0, 0);

    public bool IsNonNegative =>
        ToolCalls >= 0 &&
        ModelCalls >= 0 &&
        InputTokens >= 0 &&
        OutputTokens >= 0 &&
        ElapsedMilliseconds >= 0 &&
        CostAmount >= 0 &&
        RetryCount >= 0 &&
        ArtifactCount >= 0 &&
        ArtifactBytes >= 0;

    public bool IsWithin(AgentRunBudgetCharge upperBound) =>
        IsNonNegative &&
        ToolCalls <= upperBound.ToolCalls &&
        ModelCalls <= upperBound.ModelCalls &&
        InputTokens <= upperBound.InputTokens &&
        OutputTokens <= upperBound.OutputTokens &&
        ElapsedMilliseconds <= upperBound.ElapsedMilliseconds &&
        CostAmount <= upperBound.CostAmount &&
        RetryCount <= upperBound.RetryCount &&
        ArtifactCount <= upperBound.ArtifactCount &&
        ArtifactBytes <= upperBound.ArtifactBytes;
}

public sealed class AgentNodeRun : BaseEntity<AgentNodeRunId>
{
    private AgentNodeRun()
    {
    }

    public AgentNodeRun(
        AgentTaskId taskId,
        AgentTaskRunAttemptId runAttemptId,
        AgentTaskRunQueueItemId queueItemId,
        string planDigest,
        string executionSnapshotDigest,
        string nodeId,
        string nodeKind,
        string? toolCode,
        string dependenciesJson,
        string inputJson,
        string inputDigest,
        string outputSchemaRef,
        bool isRequired,
        bool requiresApproval,
        AgentNodeSideEffectClass sideEffectClass,
        string idempotencyKeyHash,
        int maxAttempts,
        int timeoutSeconds,
        AgentNodeBudgetLimits budget,
        string? joinPolicy,
        DateTimeOffset nowUtc)
    {
        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }

        if (timeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        Id = AgentNodeRunId.New();
        TaskId = taskId;
        RunAttemptId = runAttemptId;
        QueueItemId = queueItemId;
        PlanDigest = NormalizeRequired(planDigest, nameof(planDigest), 128);
        ExecutionSnapshotDigest = NormalizeRequired(executionSnapshotDigest, nameof(executionSnapshotDigest), 128);
        NodeId = NormalizeRequired(nodeId, nameof(nodeId), 160);
        NodeKind = NormalizeRequired(nodeKind, nameof(nodeKind), 80);
        ToolCode = NormalizeOptional(toolCode, 120);
        DependenciesJson = NormalizeRequired(dependenciesJson, nameof(dependenciesJson));
        InputJson = NormalizeRequired(inputJson, nameof(inputJson));
        InputDigest = NormalizeRequired(inputDigest, nameof(inputDigest), 128);
        OutputSchemaRef = NormalizeRequired(outputSchemaRef, nameof(outputSchemaRef), 160);
        IsRequired = isRequired;
        RequiresApproval = requiresApproval;
        JoinPolicy = NormalizeJoinPolicy(joinPolicy);
        SideEffectClass = sideEffectClass;
        IdempotencyKeyHash = NormalizeRequired(idempotencyKeyHash, nameof(idempotencyKeyHash), 128);
        MaxAttempts = maxAttempts;
        TimeoutSeconds = timeoutSeconds;
        ArgumentNullException.ThrowIfNull(budget);
        if (budget.MaxToolCalls < 0 || budget.MaxModelCalls < 0 ||
            budget.MaxInputTokens < 0 || budget.MaxOutputTokens < 0 ||
            budget.MaxCostAmount < 0 || budget.MaxArtifactCount < 0 ||
            budget.MaxArtifactBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(budget));
        }

        MaxToolCalls = budget.MaxToolCalls;
        MaxModelCalls = budget.MaxModelCalls;
        MaxInputTokens = budget.MaxInputTokens;
        MaxOutputTokens = budget.MaxOutputTokens;
        MaxCostAmount = budget.MaxCostAmount;
        MaxArtifactCount = budget.MaxArtifactCount;
        MaxArtifactBytes = budget.MaxArtifactBytes;
        Status = requiresApproval ? AgentNodeRunStatus.WaitingApproval : AgentNodeRunStatus.Pending;
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public AgentTaskId TaskId { get; private set; }

    public AgentTaskRunAttemptId RunAttemptId { get; private set; }

    public AgentTaskRunQueueItemId QueueItemId { get; private set; }

    public string PlanDigest { get; private set; } = string.Empty;

    public string ExecutionSnapshotDigest { get; private set; } = string.Empty;

    public string NodeId { get; private set; } = string.Empty;

    public string NodeKind { get; private set; } = string.Empty;

    public string? ToolCode { get; private set; }

    public string DependenciesJson { get; private set; } = "[]";

    public string InputJson { get; private set; } = "{}";

    public string InputDigest { get; private set; } = string.Empty;

    public string OutputSchemaRef { get; private set; } = string.Empty;

    public bool IsRequired { get; private set; }

    public bool RequiresApproval { get; private set; }

    public string? JoinPolicy { get; private set; }

    public AgentNodeSideEffectClass SideEffectClass { get; private set; }

    public AgentNodeRunStatus Status { get; private set; }

    public int AttemptNo { get; private set; }

    public int MaxAttempts { get; private set; }

    public int TimeoutSeconds { get; private set; }

    public int MaxToolCalls { get; private set; }

    public int MaxModelCalls { get; private set; }

    public int MaxInputTokens { get; private set; }

    public int MaxOutputTokens { get; private set; }

    public decimal MaxCostAmount { get; private set; }

    public int MaxArtifactCount { get; private set; }

    public long MaxArtifactBytes { get; private set; }

    public AgentBudgetReservationStatus BudgetReservationStatus { get; private set; }

    public long BudgetReservationNodeFencingToken { get; private set; }

    public int ReservedToolCalls { get; private set; }

    public int ReservedModelCalls { get; private set; }

    public int ReservedInputTokens { get; private set; }

    public int ReservedOutputTokens { get; private set; }

    public long ReservedElapsedMilliseconds { get; private set; }

    public decimal ReservedCostAmount { get; private set; }

    public int ReservedRetryCount { get; private set; }

    public int ReservedArtifactCount { get; private set; }

    public long ReservedArtifactBytes { get; private set; }

    public long TaskFencingToken { get; private set; }

    public long NodeFencingToken { get; private set; }

    public int IdempotencyGeneration { get; private set; }

    public Guid? LeaseId { get; private set; }

    public string? LeaseOwner { get; private set; }

    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public string IdempotencyKeyHash { get; private set; } = string.Empty;

    public string? ProviderOperationCode { get; private set; }

    public string? ProviderReceiptHash { get; private set; }

    public string? ReconciliationPolicy { get; private set; }

    public string? LastConfirmedStage { get; private set; }

    public string? IntegrityStatus { get; private set; }

    public long ReconciliationFencingToken { get; private set; }

    public int ReconciliationAttemptNo { get; private set; }

    public Guid? ReconciliationLeaseId { get; private set; }

    public string? ReconciliationOwner { get; private set; }

    public DateTimeOffset? ReconciliationLeaseExpiresAt { get; private set; }

    public DateTimeOffset? ReconciliationDeadlineAt { get; private set; }

    public bool RequiresManualResolution { get; private set; }

    public string? ReconciliationResolutionCode { get; private set; }

    public string? ReconciliationDecisionDigest { get; private set; }

    public DateTimeOffset? ReconciledAt { get; private set; }

    public string? OutputDigest { get; private set; }

    public AgentEvidenceRecordId? EvidenceId { get; private set; }

    public string? EvidenceSetDigest { get; private set; }

    public string? FailureCode { get; private set; }

    public string? SafeMessage { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsTerminal => Status is AgentNodeRunStatus.Succeeded
        or AgentNodeRunStatus.Failed
        or AgentNodeRunStatus.Cancelled;

    public AgentRunBudgetCharge CreateBudgetReservationUpperBound()
    {
        if (BudgetReservationStatus == AgentBudgetReservationStatus.Active)
        {
            throw new InvalidOperationException("NodeRun already has an active budget reservation.");
        }

        return new AgentRunBudgetCharge(
            MaxToolCalls,
            MaxModelCalls,
            MaxInputTokens,
            MaxOutputTokens,
            checked((long)TimeoutSeconds * 1000L),
            MaxCostAmount,
            AttemptNo > 0 ? 1 : 0,
            MaxArtifactCount,
            MaxArtifactBytes);
    }

    public AgentRunBudgetCharge GetActiveBudgetReservation(long nodeFencingToken)
    {
        if (BudgetReservationStatus != AgentBudgetReservationStatus.Active ||
            BudgetReservationNodeFencingToken != nodeFencingToken)
        {
            throw new InvalidOperationException("NodeRun budget reservation fencing token is stale.");
        }

        return new AgentRunBudgetCharge(
            ReservedToolCalls,
            ReservedModelCalls,
            ReservedInputTokens,
            ReservedOutputTokens,
            ReservedElapsedMilliseconds,
            ReservedCostAmount,
            ReservedRetryCount,
            ReservedArtifactCount,
            ReservedArtifactBytes);
    }

    public void BindBudgetReservation(
        long taskFencingToken,
        long nodeFencingToken,
        AgentRunBudgetCharge reservation,
        DateTimeOffset nowUtc)
    {
        EnsureFence(taskFencingToken, nodeFencingToken);
        if (Status != AgentNodeRunStatus.Claimed || !reservation.IsNonNegative)
        {
            throw new InvalidOperationException("Only a claimed NodeRun can bind a valid budget reservation.");
        }

        BudgetReservationStatus = AgentBudgetReservationStatus.Active;
        BudgetReservationNodeFencingToken = nodeFencingToken;
        ReservedToolCalls = reservation.ToolCalls;
        ReservedModelCalls = reservation.ModelCalls;
        ReservedInputTokens = reservation.InputTokens;
        ReservedOutputTokens = reservation.OutputTokens;
        ReservedElapsedMilliseconds = reservation.ElapsedMilliseconds;
        ReservedCostAmount = reservation.CostAmount;
        ReservedRetryCount = reservation.RetryCount;
        ReservedArtifactCount = reservation.ArtifactCount;
        ReservedArtifactBytes = reservation.ArtifactBytes;
        UpdatedAt = nowUtc;
    }

    public void CloseBudgetReservation(
        long nodeFencingToken,
        AgentBudgetReservationStatus terminalStatus,
        DateTimeOffset nowUtc)
    {
        if (terminalStatus is not AgentBudgetReservationStatus.Settled
            and not AgentBudgetReservationStatus.Released
            and not AgentBudgetReservationStatus.ConservativelyConsumed ||
            BudgetReservationStatus != AgentBudgetReservationStatus.Active ||
            BudgetReservationNodeFencingToken != nodeFencingToken)
        {
            throw new InvalidOperationException("NodeRun budget reservation cannot be closed under this fence.");
        }

        BudgetReservationStatus = terminalStatus;
        UpdatedAt = nowUtc;
    }

    public void BindTaskClaim(
        AgentTaskRunQueueItemId queueItemId,
        long taskFencingToken,
        DateTimeOffset nowUtc)
    {
        if (taskFencingToken <= 0 ||
            Status is AgentNodeRunStatus.Claimed or AgentNodeRunStatus.Running or AgentNodeRunStatus.OutcomeUnknown)
        {
            throw new InvalidOperationException("Only an inactive NodeRun can bind a new task claim.");
        }

        QueueItemId = queueItemId;
        TaskFencingToken = taskFencingToken;
        UpdatedAt = nowUtc;
    }

    public void MakeRunnable(DateTimeOffset nowUtc)
    {
        if (Status is not AgentNodeRunStatus.Pending and not AgentNodeRunStatus.WaitingApproval)
        {
            throw new InvalidOperationException("Only pending or approved node runs can become runnable.");
        }

        Status = AgentNodeRunStatus.Runnable;
        NextAttemptAt = null;
        UpdatedAt = nowUtc;
    }

    public void Claim(
        long taskFencingToken,
        string leaseOwner,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration)
    {
        if (Status != AgentNodeRunStatus.Runnable ||
            NextAttemptAt is not null && NextAttemptAt > nowUtc)
        {
            throw new InvalidOperationException("Only available runnable node runs can be claimed.");
        }

        if (taskFencingToken <= 0 || leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(taskFencingToken));
        }

        TaskFencingToken = taskFencingToken;
        NodeFencingToken = checked(NodeFencingToken + 1);
        AttemptNo = checked(AttemptNo + 1);
        LeaseId = Guid.NewGuid();
        LeaseOwner = NormalizeOptional(leaseOwner, 120) ?? "agent-node-runtime";
        LeaseExpiresAt = nowUtc.Add(leaseDuration);
        Status = AgentNodeRunStatus.Claimed;
        FailureCode = null;
        SafeMessage = null;
        UpdatedAt = nowUtc;
    }

    public void Start(long taskFencingToken, long nodeFencingToken, DateTimeOffset nowUtc)
    {
        EnsureFence(taskFencingToken, nodeFencingToken);
        if (Status != AgentNodeRunStatus.Claimed)
        {
            throw new InvalidOperationException("Only claimed node runs can start.");
        }

        Status = AgentNodeRunStatus.Running;
        StartedAt ??= nowUtc;
        UpdatedAt = nowUtc;
    }

    public void RecoverExpiredClaim(DateTimeOffset nowUtc)
    {
        if (Status != AgentNodeRunStatus.Claimed ||
            LeaseExpiresAt is null || LeaseExpiresAt > nowUtc)
        {
            throw new InvalidOperationException("Only an expired pre-execution node claim can be recovered.");
        }

        Status = AgentNodeRunStatus.Runnable;
        NextAttemptAt = nowUtc;
        FailureCode = "node_claim_lease_expired";
        SafeMessage = "Node claim expired before execution began; it can be reclaimed safely.";
        ReleaseLease();
        UpdatedAt = nowUtc;
    }

    public void RecoverExpiredSafeExecution(DateTimeOffset nowUtc)
    {
        if (Status != AgentNodeRunStatus.Running ||
            LeaseExpiresAt is null || LeaseExpiresAt > nowUtc ||
            SideEffectClass is not (AgentNodeSideEffectClass.ReadOnly or AgentNodeSideEffectClass.DeterministicInternal))
        {
            throw new InvalidOperationException("Only an expired safe node execution can be recovered for retry.");
        }

        Status = AgentNodeRunStatus.Runnable;
        NextAttemptAt = nowUtc;
        FailureCode = "node_worker_lost";
        SafeMessage = "Worker lease expired during a replay-safe node; retry is allowed from the last checkpoint.";
        ReleaseLease();
        UpdatedAt = nowUtc;
    }

    public void CancelBeforeExecution(string safeMessage, DateTimeOffset nowUtc)
    {
        if (Status is not AgentNodeRunStatus.Pending
            and not AgentNodeRunStatus.Runnable
            and not AgentNodeRunStatus.WaitingApproval)
        {
            throw new InvalidOperationException("Only an inactive NodeRun can be cancelled before execution.");
        }

        FailureCode = "agent_task_cancellation_requested";
        SafeMessage = NormalizeRequired(safeMessage, nameof(safeMessage), 2000);
        Status = AgentNodeRunStatus.Cancelled;
        CompletedAt = nowUtc;
        NextAttemptAt = null;
        UpdatedAt = nowUtc;
    }

    public void CancelFromDependencyFailure(string dependencyNodeId, DateTimeOffset nowUtc)
    {
        if (Status is not AgentNodeRunStatus.Pending
            and not AgentNodeRunStatus.Runnable
            and not AgentNodeRunStatus.WaitingApproval)
        {
            throw new InvalidOperationException("Only an inactive NodeRun can be cancelled by dependency failure.");
        }

        FailureCode = "agent_required_dependency_failed";
        SafeMessage = NormalizeRequired(
            $"Required dependency '{dependencyNodeId}' failed; this downstream node was not started.",
            nameof(dependencyNodeId),
            2000);
        Status = AgentNodeRunStatus.Cancelled;
        CompletedAt = nowUtc;
        NextAttemptAt = null;
        UpdatedAt = nowUtc;
    }

    public void CancelActiveKnownNoSideEffect(
        long taskFencingToken,
        long nodeFencingToken,
        string safeMessage,
        DateTimeOffset nowUtc)
    {
        EnsureFence(taskFencingToken, nodeFencingToken);
        if (Status is not AgentNodeRunStatus.Claimed and not AgentNodeRunStatus.Running ||
            Status == AgentNodeRunStatus.Running &&
            SideEffectClass is not (AgentNodeSideEffectClass.ReadOnly or AgentNodeSideEffectClass.DeterministicInternal))
        {
            throw new InvalidOperationException(
                "Only a pre-dispatch claim or a known side-effect-free running NodeRun can be cancelled.");
        }

        FailureCode = "agent_task_cancellation_requested";
        SafeMessage = NormalizeRequired(safeMessage, nameof(safeMessage), 2000);
        Status = AgentNodeRunStatus.Cancelled;
        CompletedAt = nowUtc;
        NextAttemptAt = null;
        ReleaseLease();
        UpdatedAt = nowUtc;
    }

    public void RenewLease(
        long taskFencingToken,
        long nodeFencingToken,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration)
    {
        EnsureFence(taskFencingToken, nodeFencingToken);
        if (Status is not AgentNodeRunStatus.Claimed and not AgentNodeRunStatus.Running)
        {
            throw new InvalidOperationException("Only active node runs can renew a lease.");
        }

        LeaseExpiresAt = nowUtc.Add(leaseDuration);
        UpdatedAt = nowUtc;
    }

    public void CompleteCheckpoint(
        long taskFencingToken,
        long nodeFencingToken,
        AgentEvidenceRecordId evidenceId,
        string outputDigest,
        string evidenceSetDigest,
        string? providerOperationCode,
        string? providerReceiptHash,
        DateTimeOffset nowUtc)
    {
        EnsureFence(taskFencingToken, nodeFencingToken);
        if (Status != AgentNodeRunStatus.Running)
        {
            throw new InvalidOperationException("Only running node runs can commit a success checkpoint.");
        }

        EvidenceId = evidenceId;
        OutputDigest = NormalizeRequired(outputDigest, nameof(outputDigest), 128);
        EvidenceSetDigest = NormalizeRequired(evidenceSetDigest, nameof(evidenceSetDigest), 128);
        ProviderOperationCode = NormalizeOptional(providerOperationCode, 160);
        ProviderReceiptHash = NormalizeOptional(providerReceiptHash, 128);
        Status = AgentNodeRunStatus.Succeeded;
        CompletedAt = nowUtc;
        ReleaseLease();
        UpdatedAt = nowUtc;
    }

    public void Fail(
        long taskFencingToken,
        long nodeFencingToken,
        string failureCode,
        string safeMessage,
        DateTimeOffset nowUtc,
        DateTimeOffset? retryAt = null)
    {
        EnsureFence(taskFencingToken, nodeFencingToken);
        if (Status is not AgentNodeRunStatus.Claimed and not AgentNodeRunStatus.Running)
        {
            throw new InvalidOperationException("Only active node runs can fail.");
        }

        FailureCode = NormalizeRequired(failureCode, nameof(failureCode), 120);
        SafeMessage = NormalizeRequired(safeMessage, nameof(safeMessage), 2000);
        if (retryAt.HasValue &&
            SideEffectClass is (AgentNodeSideEffectClass.ReadOnly or AgentNodeSideEffectClass.DeterministicInternal) &&
            AttemptNo < MaxAttempts)
        {
            Status = AgentNodeRunStatus.Runnable;
            NextAttemptAt = retryAt;
            ReleaseLease();
            UpdatedAt = nowUtc;
            return;
        }

        Status = AgentNodeRunStatus.Failed;
        CompletedAt = nowUtc;
        ReleaseLease();
        UpdatedAt = nowUtc;
    }

    public void MarkOutcomeUnknown(
        long taskFencingToken,
        long nodeFencingToken,
        string providerOperationCode,
        string? providerReceiptHash,
        string reconciliationPolicy,
        string lastConfirmedStage,
        string integrityStatus,
        string safeMessage,
        DateTimeOffset nowUtc,
        DateTimeOffset nextCheckAt,
        DateTimeOffset reconciliationDeadlineAt)
    {
        EnsureFence(taskFencingToken, nodeFencingToken);
        if (Status != AgentNodeRunStatus.Running ||
            SideEffectClass is AgentNodeSideEffectClass.ReadOnly or AgentNodeSideEffectClass.DeterministicInternal)
        {
            throw new InvalidOperationException("Only side-effecting running nodes can enter outcome-unknown.");
        }

        ProviderOperationCode = NormalizeRequired(providerOperationCode, nameof(providerOperationCode), 160);
        ProviderReceiptHash = NormalizeOptional(providerReceiptHash, 128);
        ReconciliationPolicy = NormalizeRequired(reconciliationPolicy, nameof(reconciliationPolicy), 120);
        LastConfirmedStage = NormalizeRequired(lastConfirmedStage, nameof(lastConfirmedStage), 120);
        IntegrityStatus = NormalizeRequired(integrityStatus, nameof(integrityStatus), 80);
        if (nextCheckAt <= nowUtc || reconciliationDeadlineAt <= nextCheckAt)
        {
            throw new ArgumentOutOfRangeException(nameof(reconciliationDeadlineAt));
        }

        SafeMessage = NormalizeRequired(safeMessage, nameof(safeMessage), 2000);
        NextAttemptAt = nextCheckAt;
        ReconciliationDeadlineAt = reconciliationDeadlineAt;
        RequiresManualResolution = false;
        Status = AgentNodeRunStatus.OutcomeUnknown;
        ReleaseLease();
        UpdatedAt = nowUtc;
    }

    public void ClaimReconciliation(
        string owner,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        bool ignoreSchedule = false)
    {
        if (Status != AgentNodeRunStatus.OutcomeUnknown ||
            !ignoreSchedule && NextAttemptAt is not null && NextAttemptAt > nowUtc ||
            ReconciliationLeaseExpiresAt is not null && ReconciliationLeaseExpiresAt > nowUtc)
        {
            throw new InvalidOperationException("Outcome-unknown node is not available for reconciliation.");
        }

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        ReconciliationFencingToken = checked(ReconciliationFencingToken + 1);
        ReconciliationAttemptNo = checked(ReconciliationAttemptNo + 1);
        ReconciliationLeaseId = Guid.NewGuid();
        ReconciliationOwner = NormalizeRequired(owner, nameof(owner), 120);
        ReconciliationLeaseExpiresAt = nowUtc.Add(leaseDuration);
        UpdatedAt = nowUtc;
    }

    public void RenewReconciliationLease(
        long taskFencingToken,
        long nodeFencingToken,
        long reconciliationFencingToken,
        Guid reconciliationLeaseId,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration)
    {
        EnsureReconciliationFence(
            taskFencingToken,
            nodeFencingToken,
            reconciliationFencingToken,
            reconciliationLeaseId);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        ReconciliationLeaseExpiresAt = nowUtc.Add(leaseDuration);
        UpdatedAt = nowUtc;
    }

    public void DeferReconciliation(
        long taskFencingToken,
        long nodeFencingToken,
        long reconciliationFencingToken,
        Guid reconciliationLeaseId,
        DateTimeOffset nowUtc,
        DateTimeOffset nextCheckAt,
        string safeMessage,
        bool conflictingEvidence)
    {
        EnsureReconciliationFence(
            taskFencingToken,
            nodeFencingToken,
            reconciliationFencingToken,
            reconciliationLeaseId);
        if (nextCheckAt <= nowUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(nextCheckAt));
        }

        SafeMessage = NormalizeRequired(safeMessage, nameof(safeMessage), 2000);
        NextAttemptAt = nextCheckAt;
        RequiresManualResolution = conflictingEvidence ||
            ReconciliationDeadlineAt is not null && nowUtc >= ReconciliationDeadlineAt;
        ReleaseReconciliationLease();
        UpdatedAt = nowUtc;
    }

    public void CompleteReconciledCheckpoint(
        long taskFencingToken,
        long nodeFencingToken,
        long reconciliationFencingToken,
        Guid reconciliationLeaseId,
        AgentEvidenceRecordId evidenceId,
        string outputDigest,
        string evidenceSetDigest,
        string? providerReceiptHash,
        string resolutionCode,
        string decisionDigest,
        DateTimeOffset nowUtc)
    {
        EnsureReconciliationFence(
            taskFencingToken,
            nodeFencingToken,
            reconciliationFencingToken,
            reconciliationLeaseId);
        EvidenceId = evidenceId;
        OutputDigest = NormalizeRequired(outputDigest, nameof(outputDigest), 128);
        EvidenceSetDigest = NormalizeRequired(evidenceSetDigest, nameof(evidenceSetDigest), 128);
        ProviderReceiptHash = NormalizeOptional(providerReceiptHash, 128);
        ReconciliationResolutionCode = NormalizeRequired(resolutionCode, nameof(resolutionCode), 120);
        ReconciliationDecisionDigest = NormalizeRequired(decisionDigest, nameof(decisionDigest), 128);
        Status = AgentNodeRunStatus.Succeeded;
        CompletedAt = nowUtc;
        ReconciledAt = nowUtc;
        NextAttemptAt = null;
        RequiresManualResolution = false;
        ReleaseReconciliationLease();
        UpdatedAt = nowUtc;
    }

    public void ResolveReconciledNotOccurred(
        long taskFencingToken,
        long nodeFencingToken,
        long reconciliationFencingToken,
        Guid reconciliationLeaseId,
        string resolutionCode,
        string decisionDigest,
        string safeMessage,
        bool allowRetry,
        DateTimeOffset nowUtc,
        DateTimeOffset? retryAt)
    {
        EnsureReconciliationFence(
            taskFencingToken,
            nodeFencingToken,
            reconciliationFencingToken,
            reconciliationLeaseId);
        ReconciliationResolutionCode = NormalizeRequired(resolutionCode, nameof(resolutionCode), 120);
        ReconciliationDecisionDigest = NormalizeRequired(decisionDigest, nameof(decisionDigest), 128);
        SafeMessage = NormalizeRequired(safeMessage, nameof(safeMessage), 2000);
        ReconciledAt = nowUtc;
        ProviderReceiptHash = null;
        if (allowRetry)
        {
            IdempotencyGeneration = checked(IdempotencyGeneration + 1);
            Status = AgentNodeRunStatus.Runnable;
            NextAttemptAt = retryAt ?? nowUtc;
            FailureCode = null;
            CompletedAt = null;
        }
        else
        {
            Status = AgentNodeRunStatus.Failed;
            FailureCode = ReconciliationResolutionCode;
            CompletedAt = nowUtc;
            NextAttemptAt = null;
        }

        RequiresManualResolution = false;
        ReleaseReconciliationLease();
        UpdatedAt = nowUtc;
    }

    public void ResolveReconciledCancelled(
        long taskFencingToken,
        long nodeFencingToken,
        long reconciliationFencingToken,
        Guid reconciliationLeaseId,
        string resolutionCode,
        string decisionDigest,
        string safeMessage,
        DateTimeOffset nowUtc)
    {
        EnsureReconciliationFence(
            taskFencingToken,
            nodeFencingToken,
            reconciliationFencingToken,
            reconciliationLeaseId);
        ReconciliationResolutionCode = NormalizeRequired(resolutionCode, nameof(resolutionCode), 120);
        ReconciliationDecisionDigest = NormalizeRequired(decisionDigest, nameof(decisionDigest), 128);
        SafeMessage = NormalizeRequired(safeMessage, nameof(safeMessage), 2000);
        Status = AgentNodeRunStatus.Cancelled;
        CompletedAt = nowUtc;
        ReconciledAt = nowUtc;
        NextAttemptAt = null;
        RequiresManualResolution = false;
        ReleaseReconciliationLease();
        UpdatedAt = nowUtc;
    }

    private void EnsureFence(long taskFencingToken, long nodeFencingToken)
    {
        if (taskFencingToken != TaskFencingToken || nodeFencingToken != NodeFencingToken)
        {
            throw new InvalidOperationException("Agent node run fencing token is stale.");
        }
    }

    private void EnsureReconciliationFence(
        long taskFencingToken,
        long nodeFencingToken,
        long reconciliationFencingToken,
        Guid reconciliationLeaseId)
    {
        EnsureFence(taskFencingToken, nodeFencingToken);
        if (Status != AgentNodeRunStatus.OutcomeUnknown ||
            reconciliationFencingToken != ReconciliationFencingToken ||
            reconciliationLeaseId == Guid.Empty ||
            ReconciliationLeaseId != reconciliationLeaseId)
        {
            throw new InvalidOperationException("Agent node reconciliation fencing token is stale.");
        }
    }

    private void ReleaseLease()
    {
        LeaseId = null;
        LeaseOwner = null;
        LeaseExpiresAt = null;
    }

    private void ReleaseReconciliationLease()
    {
        ReconciliationLeaseId = null;
        ReconciliationOwner = null;
        ReconciliationLeaseExpiresAt = null;
    }

    private static string NormalizeRequired(string value, string paramName, int? maxLength = null)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is null)
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return maxLength.HasValue && normalized.Length > maxLength.Value
            ? normalized[..maxLength.Value]
            : normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is { Length: > 0 } && normalized.Length > maxLength
            ? normalized[..maxLength]
            : normalized;
    }

    private static string? NormalizeJoinPolicy(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return value is "AllRequired" or "OptionalBestEffort"
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }
}

public sealed class AgentEvidenceRecord : BaseEntity<AgentEvidenceRecordId>
{
    public const int MaxInlinePayloadUtf8Bytes = 65_536;

    private AgentEvidenceRecord()
    {
    }

    public AgentEvidenceRecord(
        AgentEvidenceRecordId id,
        Guid? tenantId,
        Guid userId,
        SessionId sessionId,
        AgentTaskId taskId,
        AgentTaskRunAttemptId runAttemptId,
        AgentNodeRunId nodeRunId,
        string nodeId,
        AgentEvidenceKind evidenceKind,
        AgentEvidenceTruthClass truthClass,
        AgentEvidenceStorageMode storageMode,
        string canonicalEnvelopeJson,
        string envelopeDigest,
        string outputDigest,
        string? inlinePayloadJson,
        string? payloadRef,
        string mediaType,
        int byteLength,
        string payloadSha256,
        string allowedConsumerScopeJson,
        long taskFencingToken,
        long nodeFencingToken,
        DateTimeOffset nowUtc,
        DateTimeOffset? expiresAt = null)
    {
        if (userId == Guid.Empty || taskFencingToken <= 0 || nodeFencingToken <= 0)
        {
            throw new ArgumentException("Evidence authority identity and fencing tokens are required.");
        }

        if (storageMode == AgentEvidenceStorageMode.InlineCanonicalJson)
        {
            if (string.IsNullOrWhiteSpace(inlinePayloadJson) ||
                Encoding.UTF8.GetByteCount(inlinePayloadJson) > MaxInlinePayloadUtf8Bytes)
            {
                throw new ArgumentException("Inline evidence payload is missing or exceeds the v1 byte limit.");
            }
        }
        else if (string.IsNullOrWhiteSpace(payloadRef))
        {
            throw new ArgumentException("Artifact evidence payload reference is required.", nameof(payloadRef));
        }

        Id = id;
        TenantId = tenantId;
        UserId = userId;
        SessionId = sessionId;
        TaskId = taskId;
        RunAttemptId = runAttemptId;
        NodeRunId = nodeRunId;
        NodeId = Required(nodeId, nameof(nodeId), 160);
        EvidenceKind = evidenceKind;
        TruthClass = truthClass;
        StorageMode = storageMode;
        CanonicalEnvelopeJson = Required(canonicalEnvelopeJson, nameof(canonicalEnvelopeJson));
        EnvelopeDigest = Required(envelopeDigest, nameof(envelopeDigest), 128);
        OutputDigest = Required(outputDigest, nameof(outputDigest), 128);
        InlinePayloadJson = inlinePayloadJson;
        PayloadRef = Optional(payloadRef, 400);
        MediaType = Required(mediaType, nameof(mediaType), 160);
        ByteLength = byteLength;
        PayloadSha256 = Required(payloadSha256, nameof(payloadSha256), 128);
        AllowedConsumerScopeJson = Required(allowedConsumerScopeJson, nameof(allowedConsumerScopeJson));
        TaskFencingToken = taskFencingToken;
        NodeFencingToken = nodeFencingToken;
        CreatedAt = nowUtc;
        ExpiresAt = expiresAt;
    }

    public Guid? TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public SessionId SessionId { get; private set; }

    public AgentTaskId TaskId { get; private set; }

    public AgentTaskRunAttemptId RunAttemptId { get; private set; }

    public AgentNodeRunId NodeRunId { get; private set; }

    public string NodeId { get; private set; } = string.Empty;

    public AgentEvidenceKind EvidenceKind { get; private set; }

    public AgentEvidenceTruthClass TruthClass { get; private set; }

    public AgentEvidenceStorageMode StorageMode { get; private set; }

    public string CanonicalEnvelopeJson { get; private set; } = string.Empty;

    public string EnvelopeDigest { get; private set; } = string.Empty;

    public string OutputDigest { get; private set; } = string.Empty;

    public string? InlinePayloadJson { get; private set; }

    public string? PayloadRef { get; private set; }

    public string MediaType { get; private set; } = string.Empty;

    public int ByteLength { get; private set; }

    public string PayloadSha256 { get; private set; } = string.Empty;

    public string AllowedConsumerScopeJson { get; private set; } = "[]";

    public long TaskFencingToken { get; private set; }

    public long NodeFencingToken { get; private set; }

    public bool IsRevoked { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public void Revoke(DateTimeOffset nowUtc)
    {
        IsRevoked = true;
        ExpiresAt = nowUtc;
    }

    private static string Required(string value, string paramName, int? maxLength = null)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is null)
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return maxLength.HasValue && normalized.Length > maxLength.Value
            ? normalized[..maxLength.Value]
            : normalized;
    }

    private static string? Optional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is { Length: > 0 } && normalized.Length > maxLength
            ? normalized[..maxLength]
            : normalized;
    }
}

public sealed class AgentRunUsageLedgerEntry : BaseEntity<AgentRunUsageLedgerEntryId>
{
    private AgentRunUsageLedgerEntry()
    {
    }

    public AgentRunUsageLedgerEntry(
        AgentTaskId taskId,
        AgentTaskRunAttemptId runAttemptId,
        AgentNodeRunId nodeRunId,
        long taskFencingToken,
        long nodeFencingToken,
        int inputTokens,
        int outputTokens,
        int modelCalls,
        int toolCalls,
        long elapsedMilliseconds,
        decimal costAmount,
        int artifactCount,
        long artifactBytes,
        string costCurrency,
        string correlationHash,
        DateTimeOffset nowUtc)
    {
        if (taskFencingToken <= 0 || nodeFencingToken <= 0 ||
            inputTokens < 0 || outputTokens < 0 || modelCalls < 0 || toolCalls < 0 ||
            elapsedMilliseconds < 0 || costAmount < 0 || artifactCount < 0 || artifactBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(taskFencingToken));
        }

        Id = AgentRunUsageLedgerEntryId.New();
        TaskId = taskId;
        RunAttemptId = runAttemptId;
        NodeRunId = nodeRunId;
        TaskFencingToken = taskFencingToken;
        NodeFencingToken = nodeFencingToken;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        ModelCalls = modelCalls;
        ToolCalls = toolCalls;
        ElapsedMilliseconds = elapsedMilliseconds;
        CostAmount = costAmount;
        ArtifactCount = artifactCount;
        ArtifactBytes = artifactBytes;
        CostCurrency = string.IsNullOrWhiteSpace(costCurrency) ? "CNY" : costCurrency.Trim()[..Math.Min(8, costCurrency.Trim().Length)];
        CorrelationHash = string.IsNullOrWhiteSpace(correlationHash)
            ? throw new ArgumentException("Usage correlation hash is required.", nameof(correlationHash))
            : correlationHash.Trim()[..Math.Min(128, correlationHash.Trim().Length)];
        CreatedAt = nowUtc;
    }

    public AgentTaskId TaskId { get; private set; }

    public AgentTaskRunAttemptId RunAttemptId { get; private set; }

    public AgentNodeRunId NodeRunId { get; private set; }

    public long TaskFencingToken { get; private set; }

    public long NodeFencingToken { get; private set; }

    public int InputTokens { get; private set; }

    public int OutputTokens { get; private set; }

    public int ModelCalls { get; private set; }

    public int ToolCalls { get; private set; }

    public long ElapsedMilliseconds { get; private set; }

    public decimal CostAmount { get; private set; }

    public int ArtifactCount { get; private set; }

    public long ArtifactBytes { get; private set; }

    public string CostCurrency { get; private set; } = "CNY";

    public string CorrelationHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class AgentNodeReconciliationDecision : BaseEntity<AgentNodeReconciliationDecisionId>
{
    private AgentNodeReconciliationDecision()
    {
    }

    public AgentNodeReconciliationDecision(
        AgentTaskId taskId,
        AgentTaskRunAttemptId runAttemptId,
        AgentNodeRunId nodeRunId,
        long taskFencingToken,
        long nodeFencingToken,
        long reconciliationFencingToken,
        AgentOutcomeReconciliationResolution resolution,
        string reasonCode,
        string actorType,
        string actorIdHash,
        string? evidenceDigest,
        string? providerReceiptHash,
        string decisionDigest,
        DateTimeOffset decidedAtUtc)
    {
        if (taskFencingToken <= 0 || nodeFencingToken <= 0 || reconciliationFencingToken <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(taskFencingToken));
        }

        Id = AgentNodeReconciliationDecisionId.New();
        TaskId = taskId;
        RunAttemptId = runAttemptId;
        NodeRunId = nodeRunId;
        TaskFencingToken = taskFencingToken;
        NodeFencingToken = nodeFencingToken;
        ReconciliationFencingToken = reconciliationFencingToken;
        Resolution = resolution;
        ReasonCode = Required(reasonCode, nameof(reasonCode), 120);
        ActorType = Required(actorType, nameof(actorType), 40);
        ActorIdHash = Required(actorIdHash, nameof(actorIdHash), 128);
        EvidenceDigest = Optional(evidenceDigest, 128);
        ProviderReceiptHash = Optional(providerReceiptHash, 128);
        DecisionDigest = Required(decisionDigest, nameof(decisionDigest), 128);
        DecidedAtUtc = decidedAtUtc;
    }

    public AgentTaskId TaskId { get; private set; }

    public AgentTaskRunAttemptId RunAttemptId { get; private set; }

    public AgentNodeRunId NodeRunId { get; private set; }

    public long TaskFencingToken { get; private set; }

    public long NodeFencingToken { get; private set; }

    public long ReconciliationFencingToken { get; private set; }

    public AgentOutcomeReconciliationResolution Resolution { get; private set; }

    public string ReasonCode { get; private set; } = string.Empty;

    public string ActorType { get; private set; } = string.Empty;

    public string ActorIdHash { get; private set; } = string.Empty;

    public string? EvidenceDigest { get; private set; }

    public string? ProviderReceiptHash { get; private set; }

    public string DecisionDigest { get; private set; } = string.Empty;

    public DateTimeOffset DecidedAtUtc { get; private set; }

    private static string Required(string value, string paramName, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is null)
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? Optional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is null || normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }
}

public sealed class ModelQuotaReservation : BaseEntity<ModelQuotaReservationId>
{
    private ModelQuotaReservation()
    {
    }

    public ModelQuotaReservation(
        string tenantKeyHash,
        Guid? userId,
        string roleKeyHash,
        LanguageModelId modelId,
        string endpointId,
        string poolName,
        DateTimeOffset windowStartedAtUtc,
        DateTimeOffset windowEndsAtUtc,
        int estimatedInputTokens,
        int estimatedOutputTokens,
        int concurrencySlots,
        long fencingToken,
        string correlationHash,
        DateTimeOffset reservedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (windowEndsAtUtc <= windowStartedAtUtc || expiresAtUtc <= reservedAtUtc ||
            estimatedInputTokens < 0 || estimatedOutputTokens < 0 || concurrencySlots <= 0 || fencingToken <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowEndsAtUtc));
        }

        Id = ModelQuotaReservationId.New();
        TenantKeyHash = Required(tenantKeyHash, nameof(tenantKeyHash), 128);
        UserId = userId;
        RoleKeyHash = Required(roleKeyHash, nameof(roleKeyHash), 128);
        ModelId = modelId;
        EndpointId = Required(endpointId, nameof(endpointId), 160);
        PoolName = Required(poolName, nameof(poolName), 120);
        WindowStartedAtUtc = windowStartedAtUtc;
        WindowEndsAtUtc = windowEndsAtUtc;
        EstimatedInputTokens = estimatedInputTokens;
        EstimatedOutputTokens = estimatedOutputTokens;
        ConcurrencySlots = concurrencySlots;
        FencingToken = fencingToken;
        CorrelationHash = Required(correlationHash, nameof(correlationHash), 128);
        Status = ModelQuotaReservationStatus.Active;
        ReservedAtUtc = reservedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public string TenantKeyHash { get; private set; } = string.Empty;

    public Guid? UserId { get; private set; }

    public string RoleKeyHash { get; private set; } = string.Empty;

    public LanguageModelId ModelId { get; private set; }

    public string EndpointId { get; private set; } = string.Empty;

    public string PoolName { get; private set; } = string.Empty;

    public DateTimeOffset WindowStartedAtUtc { get; private set; }

    public DateTimeOffset WindowEndsAtUtc { get; private set; }

    public int EstimatedInputTokens { get; private set; }

    public int EstimatedOutputTokens { get; private set; }

    public int ActualInputTokens { get; private set; }

    public int ActualOutputTokens { get; private set; }

    public int ConcurrencySlots { get; private set; }

    public long FencingToken { get; private set; }

    public string CorrelationHash { get; private set; } = string.Empty;

    public ModelQuotaReservationStatus Status { get; private set; }

    public string? FailureCode { get; private set; }

    public DateTimeOffset ReservedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? SettledAtUtc { get; private set; }

    public void Settle(long fencingToken, int actualInputTokens, int actualOutputTokens, DateTimeOffset nowUtc)
    {
        EnsureActiveFence(fencingToken);
        if (actualInputTokens < 0 || actualOutputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actualInputTokens));
        }

        ActualInputTokens = actualInputTokens;
        ActualOutputTokens = actualOutputTokens;
        Status = ModelQuotaReservationStatus.Settled;
        SettledAtUtc = nowUtc;
    }

    public void Release(long fencingToken, DateTimeOffset nowUtc)
    {
        EnsureActiveFence(fencingToken);
        Status = ModelQuotaReservationStatus.Released;
        SettledAtUtc = nowUtc;
    }

    public void RequireReconciliation(long fencingToken, string failureCode, DateTimeOffset nowUtc)
    {
        EnsureActiveFence(fencingToken);
        FailureCode = Required(failureCode, nameof(failureCode), 120);
        Status = ModelQuotaReservationStatus.ReconciliationRequired;
        SettledAtUtc = nowUtc;
    }

    public void Expire(DateTimeOffset nowUtc)
    {
        if (Status is (ModelQuotaReservationStatus.Active or ModelQuotaReservationStatus.ReconciliationRequired) &&
            ExpiresAtUtc <= nowUtc)
        {
            Status = ModelQuotaReservationStatus.Expired;
            SettledAtUtc = nowUtc;
        }
    }

    private void EnsureActiveFence(long fencingToken)
    {
        if (Status != ModelQuotaReservationStatus.Active || fencingToken != FencingToken)
        {
            throw new InvalidOperationException("Model quota reservation fencing token is stale.");
        }
    }

    private static string Required(string value, string paramName, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is null)
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}

public sealed class ArtifactFileSetOperation : BaseEntity<ArtifactFileSetOperationId>
{
    private ArtifactFileSetOperation()
    {
    }

    public ArtifactFileSetOperation(
        Guid commitId,
        AgentTaskId taskId,
        ArtifactWorkspaceId workspaceId,
        AgentNodeRunId? nodeRunId,
        long taskFencingToken,
        long nodeFencingToken,
        string operationKind,
        string manifestJson,
        string manifestDigest,
        string stagingReference,
        DateTimeOffset nowUtc)
    {
        if (commitId == Guid.Empty || taskFencingToken < 0 || nodeFencingToken < 0)
        {
            throw new ArgumentException("Artifact file-set operation authority is invalid.");
        }

        Id = ArtifactFileSetOperationId.New();
        CommitId = commitId;
        TaskId = taskId;
        WorkspaceId = workspaceId;
        NodeRunId = nodeRunId;
        TaskFencingToken = taskFencingToken;
        NodeFencingToken = nodeFencingToken;
        OperationKind = Required(operationKind, nameof(operationKind), 80);
        ManifestJson = Required(manifestJson, nameof(manifestJson));
        ManifestDigest = Required(manifestDigest, nameof(manifestDigest), 128);
        StagingReference = Required(stagingReference, nameof(stagingReference), 500);
        Status = ArtifactFileSetOperationStatus.Prepared;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid CommitId { get; private set; }

    public AgentTaskId TaskId { get; private set; }

    public ArtifactWorkspaceId WorkspaceId { get; private set; }

    public AgentNodeRunId? NodeRunId { get; private set; }

    public long TaskFencingToken { get; private set; }

    public long NodeFencingToken { get; private set; }

    public string OperationKind { get; private set; } = string.Empty;

    public ArtifactFileSetOperationStatus Status { get; private set; }

    public string ManifestJson { get; private set; } = string.Empty;

    public string ManifestDigest { get; private set; } = string.Empty;

    public string StagingReference { get; private set; } = string.Empty;

    public string? PublishedReference { get; private set; }

    public string? PublishedManifestDigest { get; private set; }

    public string? FailureCode { get; private set; }

    public string? SafeMessage { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public void MarkPublished(string publishedReference, string publishedManifestDigest, DateTimeOffset nowUtc)
    {
        if (Status is not ArtifactFileSetOperationStatus.Prepared and not ArtifactFileSetOperationStatus.ReconciliationRequired)
        {
            throw new InvalidOperationException("Artifact file-set operation is not publishable.");
        }

        PublishedReference = Required(publishedReference, nameof(publishedReference), 500);
        PublishedManifestDigest = Required(publishedManifestDigest, nameof(publishedManifestDigest), 128);
        if (!string.Equals(ManifestDigest, PublishedManifestDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Published artifact manifest digest does not match the prepared manifest.");
        }

        Status = ArtifactFileSetOperationStatus.Published;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkDatabaseCommitted(DateTimeOffset nowUtc)
    {
        if (Status != ArtifactFileSetOperationStatus.Published)
        {
            throw new InvalidOperationException("Artifact files must be published before metadata can become authoritative.");
        }

        Status = ArtifactFileSetOperationStatus.DatabaseCommitted;
        UpdatedAtUtc = nowUtc;
    }

    public void Complete(DateTimeOffset nowUtc)
    {
        if (Status != ArtifactFileSetOperationStatus.DatabaseCommitted)
        {
            throw new InvalidOperationException("Artifact file-set operation cannot complete before its database checkpoint.");
        }

        Status = ArtifactFileSetOperationStatus.Completed;
        UpdatedAtUtc = nowUtc;
        CompletedAtUtc = nowUtc;
    }

    public void RequireReconciliation(string failureCode, string safeMessage, DateTimeOffset nowUtc)
    {
        FailureCode = Required(failureCode, nameof(failureCode), 120);
        SafeMessage = Required(safeMessage, nameof(safeMessage), 2000);
        Status = ArtifactFileSetOperationStatus.ReconciliationRequired;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkRollbackPending(string failureCode, string safeMessage, DateTimeOffset nowUtc)
    {
        FailureCode = Required(failureCode, nameof(failureCode), 120);
        SafeMessage = Required(safeMessage, nameof(safeMessage), 2000);
        Status = ArtifactFileSetOperationStatus.RollbackPending;
        UpdatedAtUtc = nowUtc;
    }

    public void Fail(string failureCode, string safeMessage, DateTimeOffset nowUtc)
    {
        FailureCode = Required(failureCode, nameof(failureCode), 120);
        SafeMessage = Required(safeMessage, nameof(safeMessage), 2000);
        Status = ArtifactFileSetOperationStatus.Failed;
        UpdatedAtUtc = nowUtc;
        CompletedAtUtc = nowUtc;
    }

    private static string Required(string value, string paramName, int? maxLength = null)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is null)
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return maxLength.HasValue && normalized.Length > maxLength.Value
            ? normalized[..maxLength.Value]
            : normalized;
    }
}
