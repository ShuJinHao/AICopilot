using System.Text;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentArtifactReferenceEvidenceResolver(
    IArtifactFileSetOperationStore operationStore,
    IArtifactWorkspaceFileSetStore fileSetStore)
{
    public async Task<Result<string>> ResolveDurableOutputAsync(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentNodeRun node,
        AgentEvidenceRecord evidence,
        CancellationToken cancellationToken)
    {
        if (evidence.StorageMode != AgentEvidenceStorageMode.ArtifactReference ||
            evidence.PayloadRef is null ||
            !evidence.PayloadRef.StartsWith("artifact-fileset:", StringComparison.Ordinal) ||
            !Guid.TryParseExact(evidence.PayloadRef["artifact-fileset:".Length..], "N", out var commitId) ||
            evidence.TaskId != task.Id ||
            evidence.UserId != task.UserId ||
            evidence.SessionId != task.SessionId ||
            evidence.NodeRunId != node.Id ||
            evidence.NodeId != node.NodeId ||
            evidence.IsRevoked ||
            evidence.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            return Invalid("ArtifactReference Evidence identity or lifetime is invalid.");
        }

        var snapshot = await operationStore.GetByCommitAsync(commitId, cancellationToken);
        if (snapshot is null ||
            snapshot.Operation.Status != ArtifactFileSetOperationStatus.Completed ||
            snapshot.Operation.TaskId != task.Id ||
            snapshot.Operation.WorkspaceId != workspace.Id ||
            snapshot.Operation.NodeRunId != node.Id ||
            snapshot.Operation.TaskFencingToken != evidence.TaskFencingToken ||
            snapshot.Operation.NodeFencingToken != evidence.NodeFencingToken ||
            !string.Equals(snapshot.Operation.ManifestDigest, evidence.OutputDigest, StringComparison.Ordinal) ||
            !string.Equals(snapshot.Operation.ManifestDigest, evidence.PayloadSha256, StringComparison.Ordinal) ||
            Encoding.UTF8.GetByteCount(snapshot.Operation.ManifestJson) != evidence.ByteLength)
        {
            return Invalid("ArtifactReference Evidence does not match its authoritative database operation.");
        }

        var stage = ArtifactFileSetOutcomeAuthorityProbe.RestoreStage(
            snapshot.Operation,
            snapshot.Workspace.WorkspaceCode);
        if (stage is null ||
            !await fileSetStore.VerifyPublishedAsync(stage, cancellationToken))
        {
            return Invalid("ArtifactReference Evidence file-set manifest or payload integrity is invalid.");
        }

        var durableOutput = ArtifactFileSetOutcomeAuthorityProbe.TryBuildDurableOutput(
            node.ToolCode,
            snapshot,
            stage);
        return durableOutput is null
            ? Invalid("ArtifactReference Evidence cannot reconstruct the frozen node output contract.")
            : Result.Success(durableOutput);
    }

    private static Result<string> Invalid(string detail) =>
        Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentNodeRunStateConflict,
            detail));
}
