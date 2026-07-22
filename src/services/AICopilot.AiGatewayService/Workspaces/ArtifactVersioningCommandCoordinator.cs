using System.Text;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class ArtifactVersioningCommandCoordinator(
    IRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<AgentTask> taskReadRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    IArtifactWorkspaceFileSetStore fileSetStore,
    IArtifactFileSetOperationStore fileSetOperationStore,
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
        var fileSetDraft = await ArtifactVersioningFiles.PrepareAtomicUpdateAsync(
            fileStore,
            context.Workspace.WorkspaceCode,
            context.Artifact,
            request.Content,
            request.Comment,
            cancellationToken);
        if (!fileSetDraft.IsSuccess)
        {
            return Result.From(fileSetDraft);
        }

        var stage = await fileSetStore.StageAsync(
            context.Workspace.WorkspaceCode,
            "UpdateArtifactContent",
            "draft/.committed",
            fileSetDraft.Value!.Files,
            cancellationToken,
            new ArtifactFileSetAuthority(
                context.Task.Id.Value,
                NodeRunId: null,
                context.Task.RunFencingToken,
                NodeFencingToken: 0));
        var sha256 = ArtifactVersioningFiles.ComputeSha256(request.Content);
        await CommitVersionUpdateAsync(
            context,
            stage,
            fileSetDraft.Value.CurrentRelativePath,
            oldVersion,
            sha256,
            request.Comment,
            isRestore: false,
            restoredVersion: null,
            cancellationToken);

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
        var fileSetDraft = await ArtifactVersioningFiles.PrepareAtomicUpdateAsync(
            fileStore,
            context.Workspace.WorkspaceCode,
            context.Artifact,
            source.Value!,
            request.Comment,
            cancellationToken);
        if (!fileSetDraft.IsSuccess)
        {
            return Result.From(fileSetDraft);
        }

        var stage = await fileSetStore.StageAsync(
            context.Workspace.WorkspaceCode,
            "RestoreArtifactVersion",
            "draft/.committed",
            fileSetDraft.Value!.Files,
            cancellationToken,
            new ArtifactFileSetAuthority(
                context.Task.Id.Value,
                NodeRunId: null,
                context.Task.RunFencingToken,
                NodeFencingToken: 0));
        var sha256 = ArtifactVersioningFiles.ComputeSha256(source.Value!);
        await CommitVersionUpdateAsync(
            context,
            stage,
            fileSetDraft.Value.CurrentRelativePath,
            oldVersion,
            sha256,
            request.Comment,
            isRestore: true,
            restoredVersion: request.Version,
            cancellationToken);

        var files = await fileStore.ListAsync(context.Workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(context.Workspace, context.Task, files));
    }

    private async Task CommitVersionUpdateAsync(
        ArtifactVersioningContext context,
        ArtifactFileSetStage stage,
        string currentRelativePath,
        int oldVersion,
        string sha256,
        string? comment,
        bool isRestore,
        int? restoredVersion,
        CancellationToken cancellationToken)
    {
        var published = stage.Files.Single(file =>
            file.RelativePath.EndsWith($"/{currentRelativePath}", StringComparison.Ordinal));
        await fileSetStore.ExecuteAsync(
            stage,
            async commitCancellationToken =>
            {
                var now = DateTimeOffset.UtcNow;
                context.Artifact.AddVersion(published.RelativePath, published.FileSize, now);
                var operation = new ArtifactFileSetOperation(
                    stage.CommitId,
                    context.Task.Id,
                    context.Workspace.Id,
                    nodeRunId: null,
                    context.Task.RunFencingToken,
                    nodeFencingToken: 0,
                    stage.OperationKind,
                    stage.ManifestJson,
                    stage.ManifestDigest,
                    stage.StagingReference,
                    now);
                operation.MarkPublished(stage.PublishedReference, stage.ManifestDigest, now);
                operation.MarkDatabaseCommitted(now);
                operation.Complete(now);
                fileSetOperationStore.AddCompleted(operation);

                workspaceRepository.Update(context.Workspace);
                if (isRestore)
                {
                    await auditRecorder.RecordArtifactVersionRestoredAsync(
                        context.Task,
                        context.Workspace,
                        context.Artifact,
                        restoredVersion!.Value,
                        oldVersion,
                        context.Artifact.Version,
                        sha256,
                        comment,
                        commitCancellationToken);
                }
                else
                {
                    await auditRecorder.RecordArtifactUpdatedAsync(
                        context.Task,
                        context.Workspace,
                        context.Artifact,
                        oldVersion,
                        context.Artifact.Version,
                        sha256,
                        comment,
                        commitCancellationToken);
                }

                await workspaceRepository.SaveChangesAsync(commitCancellationToken);
                return true;
            },
            cancellationToken);
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
