using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;

namespace AICopilot.Services.Contracts;

public enum AgentFencedWriteResult
{
    Succeeded = 0,
    StaleFence = 1,
    StateConflict = 2,
    Duplicate = 3
}

public sealed record DurableTaskClaim(
    AgentTaskRunQueueItem QueueItem,
    AgentTask Task,
    AgentTaskRunAttempt RunAttempt,
    long TaskFencingToken,
    Guid LeaseId,
    DateTimeOffset LeaseExpiresAt);

public sealed record AgentNodeRunSeed(
    string PlanDigest,
    string ExecutionSnapshotDigest,
    string NodeId,
    string NodeKind,
    string? ToolCode,
    string DependenciesJson,
    string InputJson,
    string InputDigest,
    string OutputSchemaRef,
    bool IsRequired,
    bool RequiresApproval,
    AgentNodeSideEffectClass SideEffectClass,
    string IdempotencyKeyHash,
    int MaxAttempts,
    int TimeoutSeconds,
    AgentNodeBudgetLimits Budget,
    string? JoinPolicy,
    bool IsInitiallyRunnable);

public sealed record AgentNodeRunClaim(
    AgentNodeRun NodeRun,
    AgentTaskRunQueueItemId QueueItemId,
    AgentTaskRunAttemptId RunAttemptId,
    long TaskFencingToken,
    long NodeFencingToken,
    Guid TaskLeaseId,
    Guid NodeLeaseId,
    DateTimeOffset LeaseExpiresAt);

public enum AgentNodeRunClaimOutcomeCode
{
    Claimed = 0,
    NoneAvailable = 1,
    StaleTaskFence = 2,
    BudgetNotInitialized = 3,
    WorkflowElapsedExceeded = 4,
    ToolCallsExceeded = 5,
    ModelCallsExceeded = 6,
    InputTokensExceeded = 7,
    OutputTokensExceeded = 8,
    ElapsedUsageExceeded = 9,
    CostExceeded = 10,
    RetriesExceeded = 11,
    ArtifactCountExceeded = 12,
    ArtifactBytesExceeded = 13
}

public sealed record AgentNodeRunClaimOutcome(
    AgentNodeRunClaimOutcomeCode Code,
    AgentNodeRunClaim? Claim,
    string SafeReason);

public sealed record AgentFinalizationArtifactBinding(
    ArtifactId ArtifactId,
    string SourceRelativePath,
    string FinalRelativePath,
    long FileSize,
    string MimeType,
    string Sha256);

public sealed record AgentNodeFinalizationMutation(
    ArtifactWorkspaceId WorkspaceId,
    ApprovalRequestId ApprovalRequestId,
    AgentStepId FinalStepId,
    ArtifactFileSetStage FileSetStage,
    IReadOnlyList<AgentFinalizationArtifactBinding> ArtifactBindings,
    string FinalStepOutputJson,
    string FinalSummary);

public sealed record AgentNodeSuccessCheckpoint(
    AgentTaskId TaskId,
    AgentTaskRunAttemptId RunAttemptId,
    AgentNodeRunId NodeRunId,
    long TaskFencingToken,
    long NodeFencingToken,
    AgentEvidenceRecord Evidence,
    AgentRunUsageLedgerEntry Usage,
    string OutputDigest,
    string? ProviderOperationCode,
    string? ProviderReceiptHash,
    DateTimeOffset CompletedAtUtc,
    AgentNodeFinalizationMutation? Finalization = null);

public sealed record AgentNodeFailureCheckpoint(
    AgentTaskId TaskId,
    AgentTaskRunAttemptId RunAttemptId,
    AgentNodeRunId NodeRunId,
    long TaskFencingToken,
    long NodeFencingToken,
    string FailureCode,
    string SafeMessage,
    AgentRunUsageLedgerEntry Usage,
    DateTimeOffset FailedAtUtc,
    DateTimeOffset? RetryAtUtc);

public sealed record AgentNodeOutcomeUnknownCheckpoint(
    AgentTaskId TaskId,
    AgentTaskRunAttemptId RunAttemptId,
    AgentNodeRunId NodeRunId,
    long TaskFencingToken,
    long NodeFencingToken,
    string ProviderOperationCode,
    string? ProviderReceiptHash,
    string ReconciliationPolicy,
    string LastConfirmedStage,
    string IntegrityStatus,
    string SafeMessage,
    DateTimeOffset NextCheckAtUtc,
    DateTimeOffset ReconciliationDeadlineAtUtc,
    DateTimeOffset RecordedAtUtc);

public sealed record AgentOutcomeReconciliationClaim(
    AgentNodeRun NodeRun,
    AgentTaskId TaskId,
    AgentTaskRunAttemptId RunAttemptId,
    AgentTaskRunQueueItemId QueueItemId,
    long TaskFencingToken,
    long NodeFencingToken,
    long ReconciliationFencingToken,
    Guid ReconciliationLeaseId,
    DateTimeOffset ReconciliationLeaseExpiresAt,
    DateTimeOffset ReconciliationDeadlineAt);

public sealed record AgentOutcomeReconciliationSuccessCheckpoint(
    AgentOutcomeReconciliationClaim Claim,
    AgentEvidenceRecord Evidence,
    AgentRunUsageLedgerEntry Usage,
    string OutputDigest,
    string? ProviderReceiptHash,
    string ReasonCode,
    string ActorType,
    string ActorIdHash,
    string DecisionDigest,
    DateTimeOffset DecidedAtUtc);

public sealed record AgentOutcomeReconciliationNegativeDecision(
    AgentOutcomeReconciliationClaim Claim,
    AgentOutcomeReconciliationResolution Resolution,
    string ReasonCode,
    string SafeMessage,
    string ActorType,
    string ActorIdHash,
    string? EvidenceDigest,
    string? ProviderReceiptHash,
    string DecisionDigest,
    bool AllowNodeRetry,
    DateTimeOffset? RetryAtUtc,
    DateTimeOffset DecidedAtUtc);

public sealed record AgentOutcomeReconciliationDeferral(
    AgentOutcomeReconciliationClaim Claim,
    AgentOutcomeReconciliationResolution Resolution,
    string ReasonCode,
    string SafeMessage,
    string ActorType,
    string ActorIdHash,
    string? EvidenceDigest,
    string? ProviderReceiptHash,
    string DecisionDigest,
    DateTimeOffset NextCheckAtUtc,
    DateTimeOffset DecidedAtUtc);

public enum ModelQuotaReservationResult
{
    Granted = 0,
    RateLimited = 1,
    TokenLimited = 2,
    ConcurrencyLimited = 3,
    Duplicate = 4,
    StaleFence = 5,
    ReconciliationRequired = 6,
    PolicyUnavailable = 7
}

public sealed record ModelQuotaReservationRequest(
    string TenantKeyHash,
    Guid? UserId,
    string RoleKeyHash,
    LanguageModelId ModelId,
    string EndpointId,
    string PoolName,
    int EstimatedInputTokens,
    int EstimatedOutputTokens,
    int ConcurrencySlots,
    int EndpointRpmLimit,
    int EndpointTpmLimit,
    int EndpointConcurrencyLimit,
    int ModelRpmLimit,
    int ModelTpmLimit,
    int ModelConcurrencyLimit,
    int UserRpmLimit,
    int UserTpmLimit,
    int UserConcurrencyLimit,
    int RoleRpmLimit,
    int RoleTpmLimit,
    int RoleConcurrencyLimit,
    int TenantRpmLimit,
    int TenantTpmLimit,
    int TenantConcurrencyLimit,
    string CorrelationHash,
    DateTimeOffset RequestedAtUtc,
    TimeSpan ReservationLease);

public sealed record ModelQuotaReservationLease(
    ModelQuotaReservationId ReservationId,
    long FencingToken,
    string CorrelationHash,
    string EndpointId,
    DateTimeOffset ExpiresAtUtc);

public sealed record ModelQuotaReservationOutcome(
    ModelQuotaReservationResult Result,
    ModelQuotaReservationLease? Lease,
    DateTimeOffset? RetryAtUtc,
    string SafeReason);

public sealed record ModelQuotaSettlement(
    ModelQuotaReservationLease Lease,
    int ActualInputTokens,
    int ActualOutputTokens,
    bool WasDispatched,
    bool OutcomeKnown,
    string? FailureCode,
    DateTimeOffset SettledAtUtc);

public interface IAgentDurableTaskClaimStore
{
    Task<DurableTaskClaim?> TryClaimNextAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<AgentFencedWriteResult> TryMarkStartedAsync(
        DurableTaskClaim claim,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default);

    Task<AgentFencedWriteResult> TryCompleteAsync(
        DurableTaskClaim claim,
        AgentTaskRunQueueStatus terminalStatus,
        string? failureCode,
        string safeMessage,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default);

    Task<int> RecoverExpiredStartedAsync(
        DateTimeOffset nowUtc,
        int maxItems,
        CancellationToken cancellationToken = default);
}

public interface IAgentNodeRunStore
{
    Task<IReadOnlyCollection<AgentNodeRun>> EnsureMaterializedAsync(
        DurableTaskClaim claim,
        AgentRunBudgetLimits taskBudget,
        IReadOnlyCollection<AgentNodeRunSeed> seeds,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AgentNodeRun>> ListByAttemptAsync(
        AgentTaskRunAttemptId runAttemptId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AgentEvidenceRecord>> ListEvidenceByAttemptAsync(
        AgentTaskRunAttemptId runAttemptId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AgentRunUsageLedgerEntry>> ListUsageByAttemptAsync(
        AgentTaskRunAttemptId runAttemptId,
        CancellationToken cancellationToken = default);

    Task<AgentFencedWriteResult> TryReleaseApprovalAsync(
        AgentNodeRunId nodeRunId,
        AgentTaskRunAttemptId runAttemptId,
        long taskFencingToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}

public interface IAgentNodeRunClaimStore
{
    Task<AgentNodeRunClaimOutcome> TryClaimAsync(
        AgentNodeRunId nodeRunId,
        AgentTaskRunAttemptId runAttemptId,
        long taskFencingToken,
        string leaseOwner,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<AgentNodeRunClaimOutcome> TryClaimNextAsync(
        AgentTaskRunAttemptId runAttemptId,
        long taskFencingToken,
        string leaseOwner,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<AgentFencedWriteResult> TryMarkRunningAsync(
        AgentNodeRunClaim claim,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default);

    Task<AgentFencedWriteResult> TryRenewTaskAndNodeLeaseAsync(
        AgentNodeRunClaim claim,
        TimeSpan taskLeaseDuration,
        TimeSpan nodeLeaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}

public interface IAgentNodeCheckpointStore
{
    Task<AgentFencedWriteResult> CommitSuccessAsync(
        AgentNodeSuccessCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    Task<AgentFencedWriteResult> CommitFailureAsync(
        AgentNodeFailureCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    Task<AgentFencedWriteResult> CommitOutcomeUnknownAsync(
        AgentNodeOutcomeUnknownCheckpoint checkpoint,
        CancellationToken cancellationToken = default);
}

public interface IAgentNodeOutcomeReconciliationStore
{
    Task<AgentOutcomeReconciliationClaim?> TryClaimNextAsync(
        string owner,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<AgentOutcomeReconciliationClaim?> TryClaimAsync(
        AgentNodeRunId nodeRunId,
        string owner,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<AgentFencedWriteResult> TryRenewLeaseAsync(
        AgentOutcomeReconciliationClaim claim,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<AgentFencedWriteResult> CommitSucceededAsync(
        AgentOutcomeReconciliationSuccessCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    Task<AgentFencedWriteResult> CommitNegativeDecisionAsync(
        AgentOutcomeReconciliationNegativeDecision decision,
        CancellationToken cancellationToken = default);

    Task<AgentFencedWriteResult> DeferAsync(
        AgentOutcomeReconciliationDeferral deferral,
        CancellationToken cancellationToken = default);
}

public interface IModelQuotaReservationStore
{
    Task<ModelQuotaReservationOutcome> TryReserveAsync(
        ModelQuotaReservationRequest request,
        CancellationToken cancellationToken = default);

    Task<ModelQuotaReservationResult> SettleAsync(
        ModelQuotaSettlement settlement,
        CancellationToken cancellationToken = default);

    Task<int> ReclaimExpiredAsync(
        DateTimeOffset nowUtc,
        int maxItems,
        CancellationToken cancellationToken = default);
}
