using System.Diagnostics;
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
        var started = Stopwatch.GetTimestamp();
        var result = await checkpointStore.CommitSuccessAsync(checkpoint, cancellationToken);
        return CompleteCheckpoint(
            result, started, duplicateIsSuccess: true,
            () => AgentRuntimeTelemetry.RecordUsage(checkpoint.Usage));
    }

    public async Task<Result> CommitFailureAsync(
        AgentNodeFailureCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await checkpointStore.CommitFailureAsync(checkpoint, cancellationToken);
        RecordCheckpoint(result, started);
        if (result == AgentFencedWriteResult.Succeeded)
        {
            AgentRuntimeTelemetry.RecordUsage(checkpoint.Usage);
        }

        return Map(result, duplicateIsSuccess: false);
    }

    public async Task<Result> CommitOutcomeUnknownAsync(
        AgentNodeOutcomeUnknownCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await checkpointStore.CommitOutcomeUnknownAsync(checkpoint, cancellationToken);
        return CompleteCheckpoint(
            result, started, duplicateIsSuccess: false,
            AgentRuntimeTelemetry.RecordOutcomeUnknown);
    }

    public Task<Result> CommitFinalizationConflictAsync(
        DurableTaskClaim taskClaim,
        AgentNodeRunClaim nodeClaim,
        string safeMessage,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken)
    {
        var normalizedMessage = string.IsNullOrWhiteSpace(safeMessage)
            ? "Final-output authority conflict requires manual reconciliation."
            : safeMessage.Trim();
        if (normalizedMessage.Length > 2000)
        {
            normalizedMessage = normalizedMessage[..2000];
        }

        return CommitOutcomeUnknownAsync(
            new AgentNodeOutcomeUnknownCheckpoint(
                taskClaim.Task.Id,
                taskClaim.RunAttempt.Id,
                nodeClaim.NodeRun.Id,
                taskClaim.TaskFencingToken,
                nodeClaim.NodeFencingToken,
                nodeClaim.NodeRun.ToolCode ?? "finalize_artifacts",
                ProviderReceiptHash: null,
                ReconciliationPolicy: "manual-final-output-authority-conflict-v1",
                LastConfirmedStage: "approval-decision-committed-finalization-not-committed",
                IntegrityStatus: "authority-conflict",
                normalizedMessage,
                recordedAtUtc.AddMinutes(1),
                recordedAtUtc.AddHours(24),
                recordedAtUtc),
            cancellationToken);
    }

    private static Result CompleteCheckpoint(
        AgentFencedWriteResult result,
        long started,
        bool duplicateIsSuccess,
        Action recordSuccess)
    {
        RecordCheckpoint(result, started);
        if (result == AgentFencedWriteResult.Succeeded)
        {
            recordSuccess();
        }

        return Map(result, duplicateIsSuccess);
    }

    private static void RecordCheckpoint(AgentFencedWriteResult result, long started)
    {
        AgentRuntimeTelemetry.RecordCheckpointLatency(
            Stopwatch.GetElapsedTime(started),
            result.ToString());
        if (result is AgentFencedWriteResult.StaleFence or AgentFencedWriteResult.Duplicate)
        {
            AgentRuntimeTelemetry.RecordStaleWorkerReject("node-checkpoint");
        }
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
