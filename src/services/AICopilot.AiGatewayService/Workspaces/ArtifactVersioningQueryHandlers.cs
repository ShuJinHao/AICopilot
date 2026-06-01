using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class GetArtifactContentQueryHandler(
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<AgentTask> taskRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : IQueryHandler<GetArtifactContentQuery, Result<ArtifactContentDto>>
{
    public async Task<Result<ArtifactContentDto>> Handle(
        GetArtifactContentQuery request,
        CancellationToken cancellationToken)
    {
        var access = await ArtifactVersioningAccess.LoadArtifactForReadAsync(
            workspaceRepository,
            taskRepository,
            approvalRepository,
            currentUser,
            identityAccessService,
            request.Id,
            AgentApprovalPermissions.GetWorkspace,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var context = access.Value!;
        var textError = ArtifactVersioningPolicy.ValidateTextArtifact(context.Artifact);
        if (textError is not null)
        {
            return Result.Invalid(textError);
        }

        var content = await ArtifactVersioningFiles.ReadTextAsync(
            fileStore,
            context.Workspace.WorkspaceCode,
            context.Artifact.RelativePath,
            context.Artifact.MimeType,
            ArtifactVersioningPolicy.MaxContentBytes,
            cancellationToken);
        if (!content.IsSuccess)
        {
            return Result.From(content);
        }

        var editable = ArtifactVersioningPolicy.CanEdit(
            context.Workspace,
            context.Task,
            context.Artifact,
            context.IsOwner && AgentApprovalPermissions.HasPermission(context.CurrentAccess, AgentApprovalPermissions.EditArtifact),
            hasFinalOutputApproval: await WorkspaceAccess.HasFinalOutputApprovalAsync(
                approvalRepository,
                context.Task.Id,
                context.Workspace.WorkspaceCode,
                cancellationToken));

        return Result.Success(new ArtifactContentDto(
            context.Artifact.Id.Value,
            context.Workspace.WorkspaceCode,
            context.Artifact.Name,
            context.Artifact.ArtifactType.ToString(),
            context.Artifact.Status.ToString(),
            context.Artifact.RelativePath,
            context.Artifact.Version,
            context.Artifact.MimeType,
            content.Value!,
            context.Artifact.UpdatedAt,
            editable));
    }
}

public sealed class GetArtifactVersionsQueryHandler(
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<AgentTask> taskRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : IQueryHandler<GetArtifactVersionsQuery, Result<IReadOnlyCollection<ArtifactVersionDto>>>
{
    public async Task<Result<IReadOnlyCollection<ArtifactVersionDto>>> Handle(
        GetArtifactVersionsQuery request,
        CancellationToken cancellationToken)
    {
        var access = await ArtifactVersioningAccess.LoadArtifactForReadAsync(
            workspaceRepository,
            taskRepository,
            approvalRepository,
            currentUser,
            identityAccessService,
            request.Id,
            AgentApprovalPermissions.GetWorkspace,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var context = access.Value!;
        var textError = ArtifactVersioningPolicy.ValidateTextArtifact(context.Artifact);
        if (textError is not null)
        {
            return Result.Invalid(textError);
        }

        var versions = await ArtifactVersioningFiles.ListVersionsAsync(
            fileStore,
            context.Workspace.WorkspaceCode,
            context.Artifact,
            cancellationToken);
        if (!versions.IsSuccess)
        {
            return Result.From(versions);
        }

        return Result.Success<IReadOnlyCollection<ArtifactVersionDto>>(versions.Value!);
    }
}

public sealed class DownloadArtifactVersionQueryHandler(
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<AgentTask> taskRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    AgentAuditRecorder auditRecorder,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : IQueryHandler<DownloadArtifactVersionQuery, Result<ArtifactDownloadDto>>
{
    public async Task<Result<ArtifactDownloadDto>> Handle(
        DownloadArtifactVersionQuery request,
        CancellationToken cancellationToken)
    {
        var access = await ArtifactVersioningAccess.LoadArtifactForReadAsync(
            workspaceRepository,
            taskRepository,
            approvalRepository,
            currentUser,
            identityAccessService,
            request.Id,
            AgentApprovalPermissions.DownloadArtifact,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var context = access.Value!;
        var textError = ArtifactVersioningPolicy.ValidateTextArtifact(context.Artifact);
        if (textError is not null)
        {
            return Result.Invalid(textError);
        }

        var file = await ArtifactVersioningFiles.OpenVersionAsync(
            fileStore,
            context.Workspace.WorkspaceCode,
            context.Artifact,
            request.Version,
            cancellationToken);
        if (!file.IsSuccess)
        {
            return Result.From(file);
        }

        await auditRecorder.RecordArtifactVersionDownloadAsync(
            context.Task,
            context.Workspace,
            context.Artifact,
            request.Version,
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
        return Result.Success(file.Value!);
    }
}

public sealed class GetArtifactVersionDiffQueryHandler(
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<AgentTask> taskRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : IQueryHandler<GetArtifactVersionDiffQuery, Result<ArtifactTextDiffDto>>
{
    public async Task<Result<ArtifactTextDiffDto>> Handle(
        GetArtifactVersionDiffQuery request,
        CancellationToken cancellationToken)
    {
        var access = await ArtifactVersioningAccess.LoadArtifactForReadAsync(
            workspaceRepository,
            taskRepository,
            approvalRepository,
            currentUser,
            identityAccessService,
            request.Id,
            AgentApprovalPermissions.GetWorkspace,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var context = access.Value!;
        var textError = ArtifactVersioningPolicy.ValidateTextArtifact(context.Artifact);
        if (textError is not null)
        {
            return Result.Invalid(textError);
        }

        var from = await ArtifactVersioningFiles.ReadVersionTextAsync(
            fileStore,
            context.Workspace.WorkspaceCode,
            context.Artifact,
            request.FromVersion,
            ArtifactVersioningPolicy.MaxDiffBytes,
            cancellationToken);
        if (!from.IsSuccess)
        {
            return Result.From(from);
        }

        var to = await ArtifactVersioningFiles.ReadVersionTextAsync(
            fileStore,
            context.Workspace.WorkspaceCode,
            context.Artifact,
            request.ToVersion,
            ArtifactVersioningPolicy.MaxDiffBytes,
            cancellationToken);
        if (!to.IsSuccess)
        {
            return Result.From(to);
        }

        return ArtifactTextDiffer.Diff(context.Artifact.Id.Value, request.FromVersion, from.Value!, request.ToVersion, to.Value!);
    }
}
