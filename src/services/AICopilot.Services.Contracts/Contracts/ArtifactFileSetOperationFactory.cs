using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;

namespace AICopilot.Services.Contracts;

public static class ArtifactFileSetOperationFactory
{
    public static ArtifactFileSetOperation CreateCompleted(
        ArtifactFileSetStage stage,
        AgentTaskId taskId,
        ArtifactWorkspaceId workspaceId,
        AgentNodeRunId? nodeRunId,
        long taskFencingToken,
        long nodeFencingToken,
        DateTimeOffset nowUtc)
    {
        var operation = new ArtifactFileSetOperation(
            stage.CommitId,
            taskId,
            workspaceId,
            nodeRunId,
            taskFencingToken,
            nodeFencingToken,
            stage.OperationKind,
            stage.ManifestJson,
            stage.ManifestDigest,
            stage.StagingReference,
            nowUtc);
        operation.MarkPublished(stage.PublishedReference, stage.ManifestDigest, nowUtc);
        operation.MarkDatabaseCommitted(nowUtc);
        operation.Complete(nowUtc);
        return operation;
    }
}
