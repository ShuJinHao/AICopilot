using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class DurableTaskClaimCoordinator(
    IAgentDurableTaskClaimStore claimStore)
{
    public async Task<Result<DurableTaskClaim?>> ClaimNextAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner) || leaseDuration <= TimeSpan.Zero)
        {
            return Result.Invalid("Durable task claim owner and positive lease duration are required.");
        }

        var claim = await claimStore.TryClaimNextAsync(
            leaseOwner,
            leaseDuration,
            cancellationToken);
        return Result.Success<DurableTaskClaim?>(claim);
    }

    public async Task<Result> MarkStartedAsync(
        DurableTaskClaim claim,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await claimStore.TryMarkStartedAsync(claim, startedAtUtc, cancellationToken);
        return MapFencedWrite(result, "Durable task claim could not enter Started state.");
    }

    public async Task<Result> CompleteAsync(
        DurableTaskClaim claim,
        AgentTaskRunQueueStatus terminalStatus,
        string? failureCode,
        string safeMessage,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await claimStore.TryCompleteAsync(
            claim,
            terminalStatus,
            failureCode,
            safeMessage,
            completedAtUtc,
            cancellationToken);
        return MapFencedWrite(result, "Durable task claim could not commit its terminal state.");
    }

    public Task<int> RecoverExpiredStartedAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        return claimStore.RecoverExpiredStartedAsync(
            nowUtc,
            maxItems: 32,
            cancellationToken);
    }

    private static Result MapFencedWrite(AgentFencedWriteResult result, string stateConflictDetail)
    {
        return result switch
        {
            AgentFencedWriteResult.Succeeded => Result.Success(),
            AgentFencedWriteResult.StaleFence => Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentTaskRunFenceStale,
                "Durable task fencing token is stale; this worker may not write authority state.")),
            AgentFencedWriteResult.Duplicate => Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentTaskRunFenceStale,
                "Durable task terminal state was already committed by another worker.")),
            _ => Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentTaskRunInProgress,
                stateConflictDetail))
        };
    }
}
