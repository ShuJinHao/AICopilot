using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class AgentArtifactWorkspaceService(
    IRepository<ArtifactWorkspace> workspaceRepository,
    IArtifactWorkspaceFileStore fileStore)
    : IAgentArtifactWorkspaceService
{
    public async Task<ArtifactWorkspace> CreateForTaskAsync(
        AgentTask task,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (task.WorkspaceId is not null)
        {
            var existing = await workspaceRepository.FirstOrDefaultAsync(
                new ArtifactWorkspaceByIdSpec(task.WorkspaceId.Value, includeArtifacts: true),
                cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }

        var workspaceCode = $"ws_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..38];
        var storage = await fileStore.CreateWorkspaceAsync(workspaceCode, task.Id.Value, cancellationToken);
        var workspace = new ArtifactWorkspace(
            task.Id,
            workspaceCode,
            storage.RootPath,
            storage.WorkspaceUrl,
            nowUtc);
        workspaceRepository.Add(workspace);
        return workspace;
    }

    public async Task<Artifact> WriteDraftTextArtifactAsync(
        ArtifactWorkspace workspace,
        ArtifactType artifactType,
        string name,
        string relativePath,
        string content,
        string mimeType,
        AgentStepId? stepId,
        ArtifactSourceMetadata? sourceMetadata,
        CancellationToken cancellationToken)
    {
        EnsureCanWriteDraftArtifact(workspace);
        var written = await fileStore.WriteTextAsync(
            workspace.WorkspaceCode,
            relativePath,
            content,
            mimeType,
            cancellationToken);
        var artifact = workspace.AddDraftArtifact(
            artifactType,
            name,
            written.RelativePath,
            written.FileSize,
            written.MimeType,
            stepId,
            DateTimeOffset.UtcNow);
        artifact.ApplySourceMetadata(sourceMetadata);
        workspaceRepository.Update(workspace);
        return artifact;
    }

    public async Task<Artifact> WriteDraftBinaryArtifactAsync(
        ArtifactWorkspace workspace,
        ArtifactType artifactType,
        string name,
        string relativePath,
        byte[] content,
        string mimeType,
        AgentStepId? stepId,
        ArtifactSourceMetadata? sourceMetadata,
        CancellationToken cancellationToken)
    {
        EnsureCanWriteDraftArtifact(workspace);
        var written = await fileStore.WriteBytesAsync(
            workspace.WorkspaceCode,
            relativePath,
            content,
            mimeType,
            cancellationToken);
        var artifact = workspace.AddDraftArtifact(
            artifactType,
            name,
            written.RelativePath,
            written.FileSize,
            written.MimeType,
            stepId,
            DateTimeOffset.UtcNow);
        artifact.ApplySourceMetadata(sourceMetadata);
        workspaceRepository.Update(workspace);
        return artifact;
    }

    private static void EnsureCanWriteDraftArtifact(ArtifactWorkspace workspace)
    {
        if (workspace.Status == ArtifactWorkspaceStatus.Finalized)
        {
            throw new InvalidOperationException($"{AppProblemCodes.ArtifactFinalized}: Finalized workspaces cannot receive draft artifacts.");
        }
    }
}
