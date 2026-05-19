using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed record ArtifactContentDto(
    Guid Id,
    string WorkspaceCode,
    string Name,
    string Type,
    string Status,
    string RelativePath,
    int Version,
    string MimeType,
    string Content,
    DateTimeOffset UpdatedAt,
    bool Editable);

public sealed record UpdateArtifactContentRequest(string Content, int ExpectedVersion, string? Comment = null);

public sealed record ArtifactVersionDto(
    int Version,
    string FileName,
    long FileSize,
    string MimeType,
    string Sha256,
    DateTimeOffset CreatedAt,
    bool IsCurrent,
    string DownloadUrl);

public sealed record ArtifactTextDiffEntryDto(
    string Kind,
    int? OldLine,
    int? NewLine,
    string? OldText,
    string? NewText);

public sealed record ArtifactTextDiffDto(
    Guid ArtifactId,
    int FromVersion,
    int ToVersion,
    int FromLineCount,
    int ToLineCount,
    IReadOnlyCollection<ArtifactTextDiffEntryDto> Entries,
    bool Truncated);

public sealed record RestoreArtifactVersionRequest(int ExpectedVersion, string? Comment = null);

[AuthorizeRequirement("AiGateway.GetWorkspace")]
public sealed record GetArtifactContentQuery(Guid Id) : IQuery<Result<ArtifactContentDto>>;

[AuthorizeRequirement("AiGateway.EditArtifact")]
public sealed record UpdateArtifactContentCommand(Guid Id, string Content, int ExpectedVersion, string? Comment)
    : ICommand<Result<ArtifactWorkspaceDto>>;

[AuthorizeRequirement("AiGateway.GetWorkspace")]
public sealed record GetArtifactVersionsQuery(Guid Id) : IQuery<Result<IReadOnlyCollection<ArtifactVersionDto>>>;

[AuthorizeRequirement("AiGateway.DownloadArtifact")]
public sealed record DownloadArtifactVersionQuery(Guid Id, int Version) : IQuery<Result<ArtifactDownloadDto>>;

[AuthorizeRequirement("AiGateway.GetWorkspace")]
public sealed record GetArtifactVersionDiffQuery(Guid Id, int FromVersion, int ToVersion)
    : IQuery<Result<ArtifactTextDiffDto>>;

[AuthorizeRequirement("AiGateway.EditArtifact")]
public sealed record RestoreArtifactVersionCommand(Guid Id, int Version, int ExpectedVersion, string? Comment)
    : ICommand<Result<ArtifactWorkspaceDto>>;

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

internal sealed record ArtifactVersioningContext(
    ArtifactWorkspace Workspace,
    AgentTask Task,
    Artifact Artifact,
    CurrentUserAccess CurrentAccess,
    bool IsOwner);

internal static class ArtifactVersioningAccess
{
    public static async Task<Result<ArtifactVersioningContext>> LoadArtifactForReadAsync(
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<AgentTask> taskRepository,
        IReadRepository<ApprovalRequest> approvalRepository,
        ICurrentUser currentUser,
        IIdentityAccessService identityAccessService,
        Guid artifactId,
        string ownerPermission,
        CancellationToken cancellationToken)
    {
        var context = await LoadArtifactAsync(
            workspaceRepository,
            taskRepository,
            currentUser,
            identityAccessService,
            artifactId,
            cancellationToken);
        if (!context.IsSuccess)
        {
            return Result.From(context);
        }

        var value = context.Value!;
        if (value.IsOwner)
        {
            return AgentApprovalPermissions.HasPermission(value.CurrentAccess, ownerPermission)
                ? Result.Success(value)
                : AgentApprovalPermissions.ForbiddenMissing(ownerPermission);
        }

        if (!AgentApprovalPermissions.CanReadFinalReviewWorkspace(value.CurrentAccess))
        {
            return Result.NotFound();
        }

        var hasFinalOutputApproval = await WorkspaceAccess.HasFinalOutputApprovalAsync(
            approvalRepository,
            value.Task.Id,
            value.Workspace.WorkspaceCode,
            cancellationToken);
        return hasFinalOutputApproval ? Result.Success(value) : Result.NotFound();
    }

    public static async Task<Result<ArtifactVersioningContext>> LoadArtifactForOwnerEditAsync(
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<AgentTask> taskRepository,
        ICurrentUser currentUser,
        IIdentityAccessService identityAccessService,
        Guid artifactId,
        CancellationToken cancellationToken)
    {
        var context = await LoadArtifactAsync(
            workspaceRepository,
            taskRepository,
            currentUser,
            identityAccessService,
            artifactId,
            cancellationToken);
        if (!context.IsSuccess)
        {
            return Result.From(context);
        }

        var value = context.Value!;
        if (!value.IsOwner)
        {
            return Result.NotFound();
        }

        return AgentApprovalPermissions.HasPermission(value.CurrentAccess, AgentApprovalPermissions.EditArtifact)
            ? Result.Success(value)
            : AgentApprovalPermissions.ForbiddenMissing(AgentApprovalPermissions.EditArtifact);
    }

    private static async Task<Result<ArtifactVersioningContext>> LoadArtifactAsync(
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<AgentTask> taskRepository,
        ICurrentUser currentUser,
        IIdentityAccessService identityAccessService,
        Guid artifactId,
        CancellationToken cancellationToken)
    {
        var currentAccessResult = await AgentApprovalPermissions.LoadCurrentUserAccessAsync(
            currentUser,
            identityAccessService,
            cancellationToken);
        if (!currentAccessResult.IsSuccess)
        {
            return Result.From(currentAccessResult);
        }

        var workspace = await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByArtifactIdSpec(new ArtifactId(artifactId)),
            cancellationToken);
        if (workspace is null)
        {
            return Result.NotFound();
        }

        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdSpec(workspace.TaskId, includeSteps: true),
            cancellationToken);
        if (task is null)
        {
            return Result.NotFound();
        }

        var artifact = workspace.Artifacts.FirstOrDefault(item => item.Id == new ArtifactId(artifactId));
        if (artifact is null)
        {
            return Result.NotFound();
        }

        var currentAccess = currentAccessResult.Value!;
        return Result.Success(new ArtifactVersioningContext(
            workspace,
            task,
            artifact,
            currentAccess,
            task.UserId == currentAccess.UserId));
    }
}

internal static class ArtifactVersioningPolicy
{
    public const int MaxContentBytes = 2 * 1024 * 1024;
    public const int MaxDiffBytes = 1024 * 1024;
    public const int MaxDiffLines = 2000;

    private static readonly ArtifactType[] TextArtifactTypes =
    [
        ArtifactType.Markdown,
        ArtifactType.Html,
        ArtifactType.Json,
        ArtifactType.Chart,
        ArtifactType.Csv
    ];

    public static string? ValidateTextArtifact(Artifact artifact)
    {
        if (!TextArtifactTypes.Contains(artifact.ArtifactType))
        {
            return "Only Markdown, HTML, JSON, Chart, and CSV draft artifacts support text content operations.";
        }

        if (artifact.Status == ArtifactStatus.Final)
        {
            return "Final artifacts are immutable and cannot be edited or restored.";
        }

        return artifact.Status is ArtifactStatus.Deleted or ArtifactStatus.Rejected
            ? $"Artifact status {artifact.Status} does not allow text content operations."
            : null;
    }

    public static async Task<Result> ValidateEditWindowAsync(
        IReadRepository<ApprovalRequest> approvalRepository,
        ArtifactWorkspace workspace,
        AgentTask task,
        Artifact artifact,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var textError = ValidateTextArtifact(artifact);
        if (textError is not null)
        {
            return Result.Invalid(textError);
        }

        if (expectedVersion != artifact.Version)
        {
            return Result.Invalid("Artifact version has changed. Refresh and retry with the latest expectedVersion.");
        }

        if (workspace.Status == ArtifactWorkspaceStatus.Finalized)
        {
            return Result.Invalid("Finalized workspaces cannot be edited.");
        }

        var hasFinalOutputApproval = await WorkspaceAccess.HasFinalOutputApprovalAsync(
            approvalRepository,
            task.Id,
            workspace.WorkspaceCode,
            cancellationToken);
        if (hasFinalOutputApproval)
        {
            return Result.Invalid("Artifact drafts are locked after final review submission.");
        }

        return task.Status != AgentTaskStatus.WorkspaceReady
            ? Result.Invalid("Artifact drafts can only be edited while the task is WorkspaceReady and before final review submission.")
            : Result.Success();
    }

    public static bool CanEdit(
        ArtifactWorkspace workspace,
        AgentTask task,
        Artifact artifact,
        bool hasEditPermission,
        bool hasFinalOutputApproval)
    {
        return hasEditPermission &&
               ValidateTextArtifact(artifact) is null &&
               workspace.Status != ArtifactWorkspaceStatus.Finalized &&
               task.Status == AgentTaskStatus.WorkspaceReady &&
               !hasFinalOutputApproval;
    }
}

internal static class ArtifactVersioningFiles
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<Result<ArtifactVersionDto[]>> ListVersionsAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        CancellationToken cancellationToken)
    {
        var versions = new List<ArtifactVersionDto>();
        for (var version = 1; version < artifact.Version; version++)
        {
            var metadata = await ReadMetadataAsync(fileStore, workspaceCode, artifact, version, cancellationToken);
            if (metadata.IsSuccess && metadata.Value is not null)
            {
                versions.Add(ToDto(artifact.Id.Value, metadata.Value, isCurrent: false));
            }
        }

        var currentFile = await fileStore.OpenReadAsync(
            workspaceCode,
            artifact.RelativePath,
            artifact.MimeType,
            cancellationToken);
        if (currentFile is null)
        {
            return Result.NotFound("Artifact current file does not exist.");
        }

        await using (currentFile.Stream)
        {
            var sha256 = await ComputeSha256Async(currentFile.Stream, cancellationToken);
            versions.Add(new ArtifactVersionDto(
                artifact.Version,
                currentFile.FileName,
                currentFile.FileSize,
                currentFile.MimeType,
                sha256,
                artifact.UpdatedAt,
                true,
                DownloadUrl(artifact.Id.Value, artifact.Version)));
        }

        return Result.Success(versions.OrderBy(item => item.Version).ToArray());
    }

    public static async Task<Result<ArtifactDownloadDto>> OpenVersionAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        int version,
        CancellationToken cancellationToken)
    {
        var path = await ResolveVersionPathAsync(fileStore, workspaceCode, artifact, version, cancellationToken);
        if (!path.IsSuccess)
        {
            return Result.From(path);
        }

        var resolved = path.Value!;
        var file = await fileStore.OpenReadAsync(
            workspaceCode,
            resolved.RelativePath,
            resolved.MimeType,
            cancellationToken);
        if (file is null)
        {
            return Result.NotFound("Artifact version file does not exist.");
        }

        return Result.Success(new ArtifactDownloadDto(file.Stream, resolved.FileName, resolved.MimeType, file.FileSize));
    }

    public static async Task<Result<string>> ReadVersionTextAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        int version,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var path = await ResolveVersionPathAsync(fileStore, workspaceCode, artifact, version, cancellationToken);
        if (!path.IsSuccess)
        {
            return Result.From(path);
        }

        var resolved = path.Value!;
        return await ReadTextAsync(
            fileStore,
            workspaceCode,
            resolved.RelativePath,
            resolved.MimeType,
            maxBytes,
            cancellationToken);
    }

    public static async Task<Result<string>> ReadTextAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        string relativePath,
        string mimeType,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var file = await fileStore.OpenReadAsync(workspaceCode, relativePath, mimeType, cancellationToken);
        if (file is null)
        {
            return Result.NotFound("Artifact file does not exist.");
        }

        await using var stream = file.Stream;
        if (file.FileSize > maxBytes)
        {
            return Result.Invalid($"Artifact text exceeds the {maxBytes} byte read limit.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Result.Success(await reader.ReadToEndAsync(cancellationToken));
    }

    public static async Task<Result<ArtifactVersionMetadata>> ArchiveCurrentVersionAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        string? comment,
        CancellationToken cancellationToken)
    {
        var content = await ReadTextAsync(
            fileStore,
            workspaceCode,
            artifact.RelativePath,
            artifact.MimeType,
            ArtifactVersioningPolicy.MaxContentBytes,
            cancellationToken);
        if (!content.IsSuccess)
        {
            return Result.From(content);
        }

        var versionRoot = GetVersionRoot(artifact, artifact.Version);
        var extension = Path.GetExtension(artifact.RelativePath);
        var contentPath = $"{versionRoot}/content{extension}";
        var written = await fileStore.WriteTextAsync(
            workspaceCode,
            contentPath,
            content.Value!,
            artifact.MimeType,
            cancellationToken);

        var metadata = new ArtifactVersionMetadata(
            artifact.Version,
            Path.GetFileName(artifact.RelativePath),
            artifact.RelativePath,
            written.RelativePath,
            written.FileSize,
            artifact.MimeType,
            ComputeSha256(content.Value!),
            DateTimeOffset.UtcNow,
            NormalizeComment(comment));
        await fileStore.WriteTextAsync(
            workspaceCode,
            GetMetadataPath(artifact, artifact.Version),
            JsonSerializer.Serialize(metadata, JsonOptions),
            "application/json",
            cancellationToken);
        return Result.Success(metadata);
    }

    public static string ComputeSha256(string content)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }

    private static async Task<Result<ResolvedVersionPath>> ResolveVersionPathAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        int version,
        CancellationToken cancellationToken)
    {
        if (version <= 0 || version > artifact.Version)
        {
            return Result.Invalid("Artifact version is out of range.");
        }

        if (version == artifact.Version)
        {
            return Result.Success(new ResolvedVersionPath(
                artifact.RelativePath,
                Path.GetFileName(artifact.RelativePath),
                artifact.MimeType));
        }

        var metadata = await ReadMetadataAsync(fileStore, workspaceCode, artifact, version, cancellationToken);
        if (!metadata.IsSuccess || metadata.Value is null)
        {
            return Result.NotFound("Artifact version metadata does not exist.");
        }

        return Result.Success(new ResolvedVersionPath(
            metadata.Value.ContentRelativePath,
            metadata.Value.FileName,
            metadata.Value.MimeType));
    }

    private static async Task<Result<ArtifactVersionMetadata?>> ReadMetadataAsync(
        IArtifactWorkspaceFileStore fileStore,
        string workspaceCode,
        Artifact artifact,
        int version,
        CancellationToken cancellationToken)
    {
        var file = await fileStore.OpenReadAsync(
            workspaceCode,
            GetMetadataPath(artifact, version),
            "application/json",
            cancellationToken);
        if (file is null)
        {
            return Result.Success<ArtifactVersionMetadata?>(null);
        }

        await using var stream = file.Stream;
        try
        {
            var metadata = await JsonSerializer.DeserializeAsync<ArtifactVersionMetadata>(
                stream,
                JsonOptions,
                cancellationToken);
            return metadata is null
                ? Result.Invalid("Artifact version metadata is empty.")
                : Result.Success<ArtifactVersionMetadata?>(metadata);
        }
        catch (JsonException)
        {
            return Result.Invalid("Artifact version metadata is invalid.");
        }
    }

    private static ArtifactVersionDto ToDto(Guid artifactId, ArtifactVersionMetadata metadata, bool isCurrent)
    {
        return new ArtifactVersionDto(
            metadata.Version,
            metadata.FileName,
            metadata.FileSize,
            metadata.MimeType,
            metadata.Sha256,
            metadata.CreatedAt,
            isCurrent,
            DownloadUrl(artifactId, metadata.Version));
    }

    private static string GetVersionRoot(Artifact artifact, int version)
    {
        return $"draft/.versions/{artifact.Id.Value:N}/v{version}";
    }

    private static string GetMetadataPath(Artifact artifact, int version)
    {
        return $"{GetVersionRoot(artifact, version)}/metadata.json";
    }

    private static string DownloadUrl(Guid artifactId, int version)
    {
        return $"/api/aigateway/artifact/{artifactId}/versions/{version}/download";
    }

    private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? NormalizeComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        var normalized = comment.Trim();
        return normalized.Length <= 300 ? normalized : normalized[..300];
    }

    private sealed record ResolvedVersionPath(string RelativePath, string FileName, string MimeType);
}

internal sealed record ArtifactVersionMetadata(
    int Version,
    string FileName,
    string OriginalRelativePath,
    string ContentRelativePath,
    long FileSize,
    string MimeType,
    string Sha256,
    DateTimeOffset CreatedAt,
    string? Comment);

internal static class ArtifactTextDiffer
{
    public static Result<ArtifactTextDiffDto> Diff(
        Guid artifactId,
        int fromVersion,
        string fromText,
        int toVersion,
        string toText)
    {
        var oldLines = SplitLines(fromText);
        var newLines = SplitLines(toText);
        if (oldLines.Length > ArtifactVersioningPolicy.MaxDiffLines ||
            newLines.Length > ArtifactVersioningPolicy.MaxDiffLines)
        {
            return Result.Invalid($"Artifact text diff exceeds the {ArtifactVersioningPolicy.MaxDiffLines} line limit.");
        }

        var rawEntries = BuildRawDiff(oldLines, newLines);
        var entries = CoalesceModifiedEntries(rawEntries);
        return Result.Success(new ArtifactTextDiffDto(
            artifactId,
            fromVersion,
            toVersion,
            oldLines.Length,
            newLines.Length,
            entries,
            false));
    }

    private static string[] SplitLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static IReadOnlyList<ArtifactTextDiffEntryDto> BuildRawDiff(string[] oldLines, string[] newLines)
    {
        var lcs = new int[oldLines.Length + 1, newLines.Length + 1];
        for (var i = oldLines.Length - 1; i >= 0; i--)
        {
            for (var j = newLines.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = oldLines[i] == newLines[j]
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var entries = new List<ArtifactTextDiffEntryDto>();
        var oldIndex = 0;
        var newIndex = 0;
        while (oldIndex < oldLines.Length && newIndex < newLines.Length)
        {
            if (oldLines[oldIndex] == newLines[newIndex])
            {
                entries.Add(new ArtifactTextDiffEntryDto(
                    "unchanged",
                    oldIndex + 1,
                    newIndex + 1,
                    oldLines[oldIndex],
                    newLines[newIndex]));
                oldIndex++;
                newIndex++;
            }
            else if (lcs[oldIndex + 1, newIndex] >= lcs[oldIndex, newIndex + 1])
            {
                entries.Add(new ArtifactTextDiffEntryDto("removed", oldIndex + 1, null, oldLines[oldIndex], null));
                oldIndex++;
            }
            else
            {
                entries.Add(new ArtifactTextDiffEntryDto("added", null, newIndex + 1, null, newLines[newIndex]));
                newIndex++;
            }
        }

        while (oldIndex < oldLines.Length)
        {
            entries.Add(new ArtifactTextDiffEntryDto("removed", oldIndex + 1, null, oldLines[oldIndex], null));
            oldIndex++;
        }

        while (newIndex < newLines.Length)
        {
            entries.Add(new ArtifactTextDiffEntryDto("added", null, newIndex + 1, null, newLines[newIndex]));
            newIndex++;
        }

        return entries;
    }

    private static IReadOnlyCollection<ArtifactTextDiffEntryDto> CoalesceModifiedEntries(
        IReadOnlyList<ArtifactTextDiffEntryDto> entries)
    {
        var result = new List<ArtifactTextDiffEntryDto>();
        var index = 0;
        while (index < entries.Count)
        {
            var removed = new List<ArtifactTextDiffEntryDto>();
            while (index < entries.Count && entries[index].Kind == "removed")
            {
                removed.Add(entries[index]);
                index++;
            }

            var added = new List<ArtifactTextDiffEntryDto>();
            while (index < entries.Count && entries[index].Kind == "added")
            {
                added.Add(entries[index]);
                index++;
            }

            var pairCount = Math.Min(removed.Count, added.Count);
            for (var pairIndex = 0; pairIndex < pairCount; pairIndex++)
            {
                result.Add(new ArtifactTextDiffEntryDto(
                    "modified",
                    removed[pairIndex].OldLine,
                    added[pairIndex].NewLine,
                    removed[pairIndex].OldText,
                    added[pairIndex].NewText));
            }

            result.AddRange(removed.Skip(pairCount));
            result.AddRange(added.Skip(pairCount));

            if (index < entries.Count && entries[index].Kind == "unchanged")
            {
                result.Add(entries[index]);
                index++;
            }
        }

        return result;
    }
}
