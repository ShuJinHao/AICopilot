namespace AICopilot.Services.Contracts;

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
