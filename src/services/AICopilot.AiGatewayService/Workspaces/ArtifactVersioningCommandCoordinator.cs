using System.Text;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class ArtifactVersioningCommandCoordinator(
    IRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<AgentTask> taskReadRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    AgentAuditRecorder auditRecorder,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
{
    public async Task<Result<ArtifactWorkspaceDto>> UpdateContentAsync(
        UpdateArtifactContentCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            return Result.Invalid("Artifact content is required.");
        }

        var contentSize = Encoding.UTF8.GetByteCount(request.Content);
        if (contentSize > ArtifactVersioningPolicy.MaxContentBytes)
        {
            return Result.Invalid($"Artifact content exceeds the {ArtifactVersioningPolicy.MaxContentBytes} byte text edit limit.");
        }

        var access = await LoadEditableArtifactAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var context = access.Value!;
        var oldVersion = context.Artifact.Version;
        var archive = await ArtifactVersioningFiles.ArchiveCurrentVersionAsync(
            fileStore,
            context.Workspace.WorkspaceCode,
            context.Artifact,
            request.Comment,
            cancellationToken);
        if (!archive.IsSuccess)
        {
            return Result.From(archive);
        }

        var written = await fileStore.WriteTextAsync(
            context.Workspace.WorkspaceCode,
            context.Artifact.RelativePath,
            request.Content,
            context.Artifact.MimeType,
            cancellationToken);
        var sha256 = ArtifactVersioningFiles.ComputeSha256(request.Content);
        context.Artifact.AddVersion(written.RelativePath, written.FileSize, DateTimeOffset.UtcNow);

        workspaceRepository.Update(context.Workspace);
        await auditRecorder.RecordArtifactUpdatedAsync(
            context.Task,
            context.Workspace,
            context.Artifact,
            oldVersion,
            context.Artifact.Version,
            sha256,
            request.Comment,
            cancellationToken);
        await workspaceRepository.SaveChangesAsync(cancellationToken);

        var files = await fileStore.ListAsync(context.Workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(context.Workspace, context.Task, files));
    }

    public async Task<Result<ArtifactWorkspaceDto>> RestoreVersionAsync(
        RestoreArtifactVersionCommand request,
        CancellationToken cancellationToken)
    {
        var access = await LoadEditableArtifactAsync(request.Id, request.ExpectedVersion, cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var context = access.Value!;
        if (request.Version == context.Artifact.Version)
        {
            return Result.Invalid("Cannot restore the current artifact version.");
        }

        var source = await ArtifactVersioningFiles.ReadVersionTextAsync(
            fileStore,
            context.Workspace.WorkspaceCode,
            context.Artifact,
            request.Version,
            ArtifactVersioningPolicy.MaxContentBytes,
            cancellationToken);
        if (!source.IsSuccess)
        {
            return Result.From(source);
        }

        var oldVersion = context.Artifact.Version;
        var archive = await ArtifactVersioningFiles.ArchiveCurrentVersionAsync(
            fileStore,
            context.Workspace.WorkspaceCode,
            context.Artifact,
            request.Comment,
            cancellationToken);
        if (!archive.IsSuccess)
        {
            return Result.From(archive);
        }

        var written = await fileStore.WriteTextAsync(
            context.Workspace.WorkspaceCode,
            context.Artifact.RelativePath,
            source.Value!,
            context.Artifact.MimeType,
            cancellationToken);
        var sha256 = ArtifactVersioningFiles.ComputeSha256(source.Value!);
        context.Artifact.AddVersion(written.RelativePath, written.FileSize, DateTimeOffset.UtcNow);

        workspaceRepository.Update(context.Workspace);
        await auditRecorder.RecordArtifactVersionRestoredAsync(
            context.Task,
            context.Workspace,
            context.Artifact,
            request.Version,
            oldVersion,
            context.Artifact.Version,
            sha256,
            request.Comment,
            cancellationToken);
        await workspaceRepository.SaveChangesAsync(cancellationToken);

        var files = await fileStore.ListAsync(context.Workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(context.Workspace, context.Task, files));
    }

    private async Task<Result<ArtifactVersioningContext>> LoadEditableArtifactAsync(
        Guid artifactId,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var access = await ArtifactVersioningAccess.LoadArtifactForOwnerEditAsync(
            workspaceRepository,
            taskReadRepository,
            currentUser,
            identityAccessService,
            artifactId,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var context = access.Value!;
        var validation = await ArtifactVersioningPolicy.ValidateEditWindowAsync(
            approvalRepository,
            context.Workspace,
            context.Task,
            context.Artifact,
            expectedVersion,
            cancellationToken);
        return validation.IsSuccess
            ? access
            : Result.From(validation);
    }
}
