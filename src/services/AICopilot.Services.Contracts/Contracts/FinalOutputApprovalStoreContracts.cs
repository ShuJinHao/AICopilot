using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;

namespace AICopilot.Services.Contracts;

public enum FinalOutputApprovalCommandStatus
{
    Created = 0,
    ExistingPending = 1,
    Approved = 2,
    Rejected = 3,
    DuplicateDecision = 4,
    DecisionConflict = 5,
    FinalizationConflict = 6,
    ApprovalRejected = 7,
    NotFound = 8
}

public sealed record FinalOutputApprovalPreparation(
    AgentTaskId TaskId,
    Guid RequestedBy,
    FinalOutputApprovalProof Proof,
    DateTimeOffset CreatedAtUtc);

public sealed record FinalOutputApprovalDecision(
    ApprovalRequestId ApprovalRequestId,
    Guid DecidedBy,
    bool IsApproved,
    string? Comment,
    FinalOutputApprovalProof? CurrentProof,
    DateTimeOffset DecidedAtUtc);

public sealed record FinalOutputApprovalCommandResult(
    FinalOutputApprovalCommandStatus Status,
    ApprovalRequest? Approval,
    AgentTask? Task,
    ArtifactWorkspace? Workspace,
    AgentTaskRunAttempt? RunAttempt,
    AgentTaskRunQueueItem? QueueItem,
    bool StateChanged);

public interface IFinalOutputApprovalStore
{
    Task<FinalOutputApprovalCommandResult> PrepareAsync(
        FinalOutputApprovalPreparation preparation,
        CancellationToken cancellationToken = default);

    Task<FinalOutputApprovalCommandResult> DecideAsync(
        FinalOutputApprovalDecision decision,
        CancellationToken cancellationToken = default);
}
