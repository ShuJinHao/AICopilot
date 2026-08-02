namespace AICopilot.Services.Contracts;

using AICopilot.Core.AiGateway.Runtime.AgentSessions;
using AICopilot.SharedKernel.Ai;

public sealed record AgentApprovalBinding(
    Guid SessionId,
    Guid UserId,
    string? TenantId,
    string RequestId,
    string ToolCallId,
    string ToolName,
    AiToolCallKind ToolKind,
    string? ServerName,
    AiToolTargetType? TargetType,
    string? TargetName,
    string? CanonicalToolName,
    IReadOnlyDictionary<string, object?> Arguments,
    int ToolSchemaVersion,
    string CanonicalArgumentsDigest);

public sealed record AgentSessionStateSnapshot(
    Guid SessionId,
    Guid UserId,
    string? TenantId,
    int AgentSchemaVersion,
    string SerializedSessionState,
    AgentSessionRuntimeStatus Status,
    Guid? ActiveTurnId,
    long Version,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<AgentApprovalBinding> PendingApprovals);

public interface IAgentSessionStateStore
{
    void AddNew(
        Guid sessionId,
        Guid userId,
        string? tenantId,
        string serializedSessionState);

    Task<AgentSessionStateSnapshot> LoadOwnedAsync(
        Guid sessionId,
        Guid userId,
        string? tenantId,
        CancellationToken cancellationToken = default);

    Task<AgentSessionStateSnapshot> BeginTurnAsync(
        Guid sessionId,
        Guid userId,
        string? tenantId,
        Guid turnId,
        bool approvalContinuation,
        CancellationToken cancellationToken = default);

    Task<AgentSessionStateSnapshot> PersistCheckpointAsync(
        Guid sessionId,
        Guid userId,
        string? tenantId,
        Guid turnId,
        string serializedSessionState,
        CancellationToken cancellationToken = default);

    Task<AgentSessionStateSnapshot> CompleteTurnAsync(
        Guid sessionId,
        Guid userId,
        string? tenantId,
        Guid turnId,
        string serializedSessionState,
        IReadOnlyCollection<AgentApprovalBinding> pendingApprovals,
        CancellationToken cancellationToken = default);

    Task InterruptTurnAsync(
        Guid sessionId,
        Guid userId,
        string? tenantId,
        Guid turnId,
        CancellationToken cancellationToken = default);

    Task<AgentSessionStateSnapshot> PersistModeChangeAsync(
        Guid sessionId,
        Guid userId,
        string? tenantId,
        long expectedVersion,
        string serializedSessionState,
        CancellationToken cancellationToken = default);
}

public enum AgentSessionStateFailure
{
    Missing = 0,
    OwnershipMismatch = 1,
    SchemaMismatch = 2,
    Corrupt = 3,
    Oversize = 4,
    Expired = 5,
    AlreadyRunning = 6,
    Interrupted = 7,
    ApprovalPending = 8,
    ApprovalMissing = 9,
    VersionConflict = 10,
    TurnMismatch = 11
}

public sealed class AgentSessionStateException(
    AgentSessionStateFailure failure,
    string safeMessage)
    : InvalidOperationException(safeMessage)
{
    public AgentSessionStateFailure Failure { get; } = failure;
}
