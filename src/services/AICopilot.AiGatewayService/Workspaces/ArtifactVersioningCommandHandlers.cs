using System.Text;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class UpdateArtifactContentCommandHandler(
    IRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<AgentTask> taskReadRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    AgentAuditRecorder auditRecorder,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : ICommandHandler<UpdateArtifactContentCommand, Result<ArtifactWorkspaceDto>>
{
    public async Task<Result<ArtifactWorkspaceDto>> Handle(
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

        var access = await ArtifactVersioningAccess.LoadArtifactForOwnerEditAsync(
            workspaceRepository,
            taskReadRepository,
            currentUser,
            identityAccessService,
            request.Id,
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
            request.ExpectedVersion,
            cancellationToken);
        if (!validation.IsSuccess)
        {
            return Result.From(validation);
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
            request.Content,
            context.Artifact.MimeType,
            cancellationToken);
        var sha256 = ArtifactVersioningFiles.ComputeSha256(request.Content);
        var now = DateTimeOffset.UtcNow;
        context.Artifact.AddVersion(written.RelativePath, written.FileSize, now);

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
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        var files = await fileStore.ListAsync(context.Workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(context.Workspace, context.Task, files));
    }
}

public sealed class RestoreArtifactVersionCommandHandler(
    IRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<AgentTask> taskReadRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    AgentAuditRecorder auditRecorder,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : ICommandHandler<RestoreArtifactVersionCommand, Result<ArtifactWorkspaceDto>>
{
    public async Task<Result<ArtifactWorkspaceDto>> Handle(
        RestoreArtifactVersionCommand request,
        CancellationToken cancellationToken)
    {
        var access = await ArtifactVersioningAccess.LoadArtifactForOwnerEditAsync(
            workspaceRepository,
            taskReadRepository,
            currentUser,
            identityAccessService,
            request.Id,
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
            request.ExpectedVersion,
            cancellationToken);
        if (!validation.IsSuccess)
        {
            return Result.From(validation);
        }

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
        var now = DateTimeOffset.UtcNow;
        context.Artifact.AddVersion(written.RelativePath, written.FileSize, now);

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
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        var files = await fileStore.ListAsync(context.Workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(context.Workspace, context.Task, files));
    }
}
