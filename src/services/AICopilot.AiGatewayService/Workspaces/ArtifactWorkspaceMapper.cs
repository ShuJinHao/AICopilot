using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.Workspaces;

internal static class ArtifactWorkspaceMapper
{
    public static ArtifactWorkspaceDto Map(
        ArtifactWorkspace workspace,
        AgentTask? task,
        IReadOnlyCollection<ArtifactWorkspaceFileItem> files)
    {
        var stepIndexes = task?.Steps.ToDictionary(step => step.Id, step => step.StepIndex)
                          ?? new Dictionary<AgentStepId, int>();
        var artifacts = workspace.Artifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(artifact => MapArtifact(artifact, stepIndexes))
            .ToArray();

        return new ArtifactWorkspaceDto(
            workspace.Id,
            workspace.WorkspaceCode,
            workspace.TaskId,
            workspace.Status.ToString(),
            files.Select(item => new ArtifactWorkspaceFileDto(
                    item.Name,
                    item.RelativePath,
                    item.IsDirectory,
                    item.FileSize,
                    item.UpdatedAt))
                .ToArray(),
            artifacts)
        {
            Manifest = artifacts
                .Select(artifact => new ArtifactManifestItemDto(
                    artifact.Id,
                    artifact.Type,
                    artifact.Name,
                    artifact.RelativePath,
                    artifact.Status,
                    artifact.Version,
                    artifact.GeneratedByStep ?? artifact.GeneratedByStepOrder,
                    artifact.DownloadUrl,
                    artifact.CreatedAt))
                .ToArray(),
            DraftArtifacts = artifacts
                .Where(artifact => artifact.Status != ArtifactStatus.Final.ToString())
                .ToArray(),
            FinalArtifacts = artifacts
                .Where(artifact => artifact.Status == ArtifactStatus.Final.ToString())
                .ToArray()
        };
    }

    private static ArtifactDto MapArtifact(
        Artifact artifact,
        IReadOnlyDictionary<AgentStepId, int> stepIndexes)
    {
        int? generatedByStep = artifact.CreatedByStepId.HasValue &&
                               stepIndexes.TryGetValue(artifact.CreatedByStepId.Value, out var stepIndex)
            ? stepIndex
            : null;
        return new ArtifactDto(
            artifact.Id,
            artifact.Name,
            artifact.ArtifactType.ToString(),
            artifact.Status.ToString(),
            artifact.RelativePath,
            artifact.FileSize,
            artifact.MimeType,
            artifact.Version,
            artifact.UpdatedAt,
            ResolvePreviewKind(artifact),
            $"/api/aigateway/artifact/{artifact.Id.Value}/download",
            generatedByStep,
            artifact.Status != ArtifactStatus.Final,
            ResolveApprovalStatus(artifact),
            artifact.FinalizedAt ?? (artifact.Status == ArtifactStatus.Final ? artifact.UpdatedAt : null),
            artifact.Version,
            ResolveArtifactStatus(artifact),
            artifact.SourceMode,
            artifact.Boundary,
            artifact.IsSimulation,
            artifact.IsSandbox,
            artifact.SourceLabel,
            artifact.QueryHash,
            artifact.ResultHash,
            artifact.RowCount,
            artifact.IsTruncated)
        {
            CreatedAt = artifact.CreatedAt,
            GeneratedByStep = generatedByStep
        };
    }

    private static string ResolvePreviewKind(Artifact artifact)
    {
        return artifact.ArtifactType switch
        {
            ArtifactType.Chart => "chart",
            ArtifactType.Json => "json",
            ArtifactType.Csv => "table",
            ArtifactType.Markdown => "markdown",
            ArtifactType.Html => "html",
            ArtifactType.Pdf => "pdf",
            ArtifactType.Pptx => "download",
            ArtifactType.Xlsx => "spreadsheet",
            _ => "download"
        };
    }

    private static string ResolveApprovalStatus(Artifact artifact)
    {
        return artifact.Status switch
        {
            ArtifactStatus.Draft or ArtifactStatus.Reviewing => "Pending",
            ArtifactStatus.Approved or ArtifactStatus.Final => "Approved",
            ArtifactStatus.Rejected => "Rejected",
            _ => artifact.Status.ToString()
        };
    }

    private static string ResolveArtifactStatus(Artifact artifact)
    {
        return artifact.Status switch
        {
            ArtifactStatus.Draft => "Draft",
            ArtifactStatus.Reviewing or ArtifactStatus.Approved => "FinalPendingApproval",
            ArtifactStatus.Final => "Final",
            ArtifactStatus.Deleted => "Deleted",
            _ => artifact.Status.ToString()
        };
    }
}
