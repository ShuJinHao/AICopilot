using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentArtifactFileSetCheckpointGate(
    IArtifactFileSetOperationStore operationStore,
    IArtifactWorkspaceFileSetStore fileSetStore)
{
    public async Task<Result> ValidateAsync(
        DurableTaskClaim taskClaim,
        AgentNodeRunClaim nodeClaim,
        ArtifactWorkspace workspace,
        AgentStep step,
        CancellationToken cancellationToken)
    {
        if (nodeClaim.NodeRun.SideEffectClass != AgentNodeSideEffectClass.ArtifactWrite)
        {
            return Result.Success();
        }

        var operations = await operationStore.ListByNodeFenceAsync(
            nodeClaim.NodeRun.Id,
            taskClaim.TaskFencingToken,
            nodeClaim.NodeFencingToken,
            cancellationToken);
        if (operations.Count == 0 &&
            workspace.Artifacts.All(artifact => artifact.CreatedByStepId != step.Id))
        {
            return Result.Success();
        }

        if (operations.Count != 1)
        {
            return Invalid("ArtifactWrite checkpoint requires exactly one fenced file-set database operation.");
        }

        var snapshot = operations.Single();
        var operation = snapshot.Operation;
        if (operation.Status != ArtifactFileSetOperationStatus.Completed ||
            operation.TaskId != taskClaim.Task.Id ||
            operation.WorkspaceId != workspace.Id ||
            operation.NodeRunId != nodeClaim.NodeRun.Id ||
            operation.TaskFencingToken != taskClaim.TaskFencingToken ||
            operation.NodeFencingToken != nodeClaim.NodeFencingToken ||
            !string.Equals(operation.ManifestDigest, operation.PublishedManifestDigest, StringComparison.Ordinal))
        {
            return Invalid("ArtifactWrite file-set database checkpoint conflicts with the active node fence.");
        }

        var stage = ArtifactFileSetOutcomeAuthorityProbe.RestoreStage(
            operation,
            snapshot.Workspace.WorkspaceCode);
        if (stage is null ||
            stage.Authority.TaskId != taskClaim.Task.Id.Value ||
            stage.Authority.NodeRunId != nodeClaim.NodeRun.Id.Value ||
            stage.Authority.TaskFencingToken != taskClaim.TaskFencingToken ||
            stage.Authority.NodeFencingToken != nodeClaim.NodeFencingToken ||
            !await fileSetStore.VerifyPublishedAsync(stage, cancellationToken))
        {
            return Invalid("ArtifactWrite file-set manifest or payload integrity is invalid.");
        }

        return Result.Success();
    }

    private static Result Invalid(string detail) =>
        Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentNodeRunStateConflict,
            detail));
}
