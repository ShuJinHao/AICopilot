using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class NodeCheckpointCoordinator(
    IAgentNodeCheckpointStore checkpointStore)
{
    public async Task<Result> CommitSuccessAsync(
        AgentNodeSuccessCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        return Map(
            await checkpointStore.CommitSuccessAsync(checkpoint, cancellationToken),
            duplicateIsSuccess: true);
    }

    public async Task<Result> CommitFailureAsync(
        AgentNodeFailureCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        return Map(
            await checkpointStore.CommitFailureAsync(checkpoint, cancellationToken),
            duplicateIsSuccess: false);
    }

    public async Task<Result> CommitOutcomeUnknownAsync(
        AgentNodeOutcomeUnknownCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        return Map(
            await checkpointStore.CommitOutcomeUnknownAsync(checkpoint, cancellationToken),
            duplicateIsSuccess: false);
    }

    private static Result Map(AgentFencedWriteResult result, bool duplicateIsSuccess)
    {
        return result switch
        {
            AgentFencedWriteResult.Succeeded => Result.Success(),
            AgentFencedWriteResult.Duplicate when duplicateIsSuccess => Result.Success(),
            AgentFencedWriteResult.StaleFence or AgentFencedWriteResult.Duplicate =>
                Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.AgentNodeRunFenceStale,
                    "Node checkpoint rejected a stale fencing token.")),
            _ => Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentNodeRunStateConflict,
                "Node checkpoint authority state is inconsistent."))
        };
    }
}
