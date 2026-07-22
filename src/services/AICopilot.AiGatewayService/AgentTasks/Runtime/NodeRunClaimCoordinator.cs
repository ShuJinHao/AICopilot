using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class NodeRunClaimCoordinator(
    IAgentNodeRunClaimStore claimStore)
{
    public async Task<Result<AgentNodeRunClaim?>> ClaimNextAsync(
        AgentTaskRunAttemptId runAttemptId,
        long taskFencingToken,
        string leaseOwner,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var outcome = await claimStore.TryClaimNextAsync(
            runAttemptId,
            taskFencingToken,
            leaseOwner,
            leaseDuration,
            nowUtc,
            cancellationToken);
        if (outcome.Code == AgentNodeRunClaimOutcomeCode.Claimed && outcome.Claim is not null)
        {
            return Result.Success<AgentNodeRunClaim?>(outcome.Claim);
        }

        if (outcome.Code == AgentNodeRunClaimOutcomeCode.NoneAvailable)
        {
            return Result.Success<AgentNodeRunClaim?>(null);
        }

        return Result.Failure(new ApiProblemDescriptor(
            outcome.Code == AgentNodeRunClaimOutcomeCode.StaleTaskFence
                ? AppProblemCodes.AgentNodeRunFenceStale
                : AppProblemCodes.AgentRunBudgetExceeded,
            outcome.SafeReason));
    }

    public async Task<Result> MarkRunningAsync(
        AgentNodeRunClaim claim,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var result = await claimStore.TryMarkRunningAsync(claim, nowUtc, cancellationToken);
        return Map(result, "NodeRun could not enter Running state.");
    }

    public async Task<Result> RenewTaskAndNodeLeaseAsync(
        AgentNodeRunClaim claim,
        TimeSpan taskLeaseDuration,
        TimeSpan nodeLeaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var result = await claimStore.TryRenewTaskAndNodeLeaseAsync(
            claim,
            taskLeaseDuration,
            nodeLeaseDuration,
            nowUtc,
            cancellationToken);
        return Map(result, "NodeRun lease could not be renewed.");
    }

    private static Result Map(AgentFencedWriteResult result, string conflictDetail)
    {
        return result switch
        {
            AgentFencedWriteResult.Succeeded => Result.Success(),
            AgentFencedWriteResult.StaleFence or AgentFencedWriteResult.Duplicate =>
                Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.AgentNodeRunFenceStale,
                    "NodeRun fencing token is stale; this worker may not write authority state.")),
            _ => Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentNodeRunStateConflict,
                conflictDetail))
        };
    }
}
