using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.CloudReadiness;
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

public sealed record ArtifactDto(
    Guid Id,
    string Name,
    string Type,
    string Status,
    string RelativePath,
    long FileSize,
    string MimeType,
    int Version,
    DateTimeOffset UpdatedAt,
    string PreviewKind,
    string DownloadUrl,
    int? GeneratedByStepOrder,
    bool RequiresApproval,
    string ApprovalStatus,
    DateTimeOffset? FinalizedAt,
    int ArtifactVersion,
    string ArtifactStatus,
    string? SourceMode,
    string? Boundary,
    bool IsSimulation,
    bool IsSandbox,
    string? SourceLabel,
    string? QueryHash,
    string? ResultHash,
    int RowCount,
    bool IsTruncated)
{
    public DateTimeOffset CreatedAt { get; init; }

    public int? GeneratedByStep { get; init; }
}

public sealed record ArtifactManifestItemDto(
    Guid ArtifactId,
    string Type,
    string Name,
    string RelativePath,
    string Status,
    int Version,
    int? GeneratedByStep,
    string DownloadUrl,
    DateTimeOffset CreatedAt);

public sealed record ArtifactWorkspaceFileDto(
    string Name,
    string RelativePath,
    bool IsDirectory,
    long FileSize,
    DateTimeOffset UpdatedAt);

public sealed record ArtifactWorkspaceDto(
    Guid Id,
    string WorkspaceCode,
    Guid TaskId,
    string Status,
    IReadOnlyCollection<ArtifactWorkspaceFileDto> Files,
    IReadOnlyCollection<ArtifactDto> Artifacts)
{
    public IReadOnlyCollection<ArtifactManifestItemDto> Manifest { get; init; } = [];

    public IReadOnlyCollection<ArtifactDto> DraftArtifacts { get; init; } = [];

    public IReadOnlyCollection<ArtifactDto> FinalArtifacts { get; init; } = [];
}

public sealed record ArtifactDownloadDto(Stream Stream, string FileName, string MimeType, long FileSize);

public sealed record ArtifactWorkspaceSettingsDto(
    string RootPath,
    IReadOnlyCollection<string> Folders,
    IReadOnlyCollection<string> AllowedArtifactTypes,
    bool AllowsUserDefinedPath);

public sealed record GetArtifactWorkspaceQuery(string Code) : IQuery<Result<ArtifactWorkspaceDto>>;

public sealed record DownloadArtifactQuery(Guid Id) : IQuery<Result<ArtifactDownloadDto>>;

[AuthorizeRequirement("AiGateway.SubmitFinalReview")]
public sealed record SubmitFinalReviewCommand(string Code) : ICommand<Result<ArtifactWorkspaceDto>>;

[AuthorizeRequirement("AiGateway.FinalizeWorkspace")]
public sealed record FinalizeArtifactWorkspaceCommand(string Code) : ICommand<Result<ArtifactWorkspaceDto>>;

[AuthorizeRequirement("AiGateway.GetWorkspace")]
public sealed record GetArtifactWorkspaceSettingsQuery : IQuery<Result<ArtifactWorkspaceSettingsDto>>;

public interface IAgentArtifactWorkspaceService
{
    Task<ArtifactWorkspace> CreateForTaskAsync(AgentTask task, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<Artifact> WriteDraftTextArtifactAsync(
        ArtifactWorkspace workspace,
        ArtifactType artifactType,
        string name,
        string relativePath,
        string content,
        string mimeType,
        AgentStepId? stepId,
        ArtifactSourceMetadata? sourceMetadata,
        CancellationToken cancellationToken);

    Task<Artifact> WriteDraftBinaryArtifactAsync(
        ArtifactWorkspace workspace,
        ArtifactType artifactType,
        string name,
        string relativePath,
        byte[] content,
        string mimeType,
        AgentStepId? stepId,
        ArtifactSourceMetadata? sourceMetadata,
        CancellationToken cancellationToken);
}

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

public sealed class GetArtifactWorkspaceSettingsQueryHandler(IArtifactWorkspaceFileStore fileStore)
    : IQueryHandler<GetArtifactWorkspaceSettingsQuery, Result<ArtifactWorkspaceSettingsDto>>
{
    public Task<Result<ArtifactWorkspaceSettingsDto>> Handle(
        GetArtifactWorkspaceSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var settings = fileStore.GetSettings();
        return Task.FromResult(Result.Success(new ArtifactWorkspaceSettingsDto(
            settings.RootPath,
            settings.Folders,
            settings.AllowedArtifactTypes,
            settings.AllowsUserDefinedPath)));
    }
}

public sealed class GetArtifactWorkspaceQueryHandler(
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<AgentTask> taskRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : IQueryHandler<GetArtifactWorkspaceQuery, Result<ArtifactWorkspaceDto>>
{
    public async Task<Result<ArtifactWorkspaceDto>> Handle(
        GetArtifactWorkspaceQuery request,
        CancellationToken cancellationToken)
    {
        var access = await WorkspaceAccess.LoadByCodeForOwnerOrPermissionAsync(
            workspaceRepository,
            taskRepository,
            currentUser,
            identityAccessService,
            request.Code,
            includeArtifacts: true,
            ownerPermission: AgentApprovalPermissions.GetWorkspace,
            privilegedPermissions: [AgentApprovalPermissions.ApproveFinalOutput, AgentApprovalPermissions.FinalizeWorkspace],
            approvalRepository,
            requireFinalOutputApprovalForPrivilegedAccess: true,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var files = await fileStore.ListAsync(access.Value!.Workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(access.Value.Workspace, access.Value.Task, files));
    }
}

public sealed class DownloadArtifactQueryHandler(
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<AgentTask> taskRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    AgentAuditRecorder auditRecorder,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : IQueryHandler<DownloadArtifactQuery, Result<ArtifactDownloadDto>>
{
    public async Task<Result<ArtifactDownloadDto>> Handle(
        DownloadArtifactQuery request,
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

        var currentAccess = currentAccessResult.Value!;
        var userId = currentAccess.UserId;
        var workspace = await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByArtifactIdSpec(new ArtifactId(request.Id)),
            cancellationToken);
        if (workspace is null)
        {
            return Result.NotFound();
        }

        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdSpec(workspace.TaskId),
            cancellationToken);
        if (task is null)
        {
            return Result.NotFound();
        }

        if (task.UserId == userId)
        {
            if (!AgentApprovalPermissions.HasPermission(currentAccess, AgentApprovalPermissions.DownloadArtifact))
            {
                return AgentApprovalPermissions.ForbiddenMissing(AgentApprovalPermissions.DownloadArtifact);
            }
        }
        else if (!AgentApprovalPermissions.CanReadFinalReviewWorkspace(currentAccess))
        {
            return Result.NotFound();
        }
        else if (!await WorkspaceAccess.HasFinalOutputApprovalAsync(
                     approvalRepository,
                     task.Id,
                     workspace.WorkspaceCode,
                     cancellationToken))
        {
            return Result.NotFound();
        }

        var artifact = workspace.Artifacts.FirstOrDefault(item => item.Id == new ArtifactId(request.Id));
        if (artifact is null)
        {
            return Result.NotFound();
        }

        var file = await fileStore.OpenReadAsync(
            workspace.WorkspaceCode,
            artifact.RelativePath,
            artifact.MimeType,
            cancellationToken);
        if (file is null)
        {
            return Result.NotFound();
        }

        await auditRecorder.RecordArtifactDownloadAsync(task, workspace, artifact, cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);
        return Result.Success(new ArtifactDownloadDto(file.Stream, file.FileName, file.MimeType, file.FileSize));
    }
}

public sealed class SubmitFinalReviewCommandHandler(
    IRepository<ArtifactWorkspace> workspaceRepository,
    IRepository<AgentTask> taskRepository,
    IRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    AgentAuditRecorder auditRecorder,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser)
    : ICommandHandler<SubmitFinalReviewCommand, Result<ArtifactWorkspaceDto>>
{
    public async Task<Result<ArtifactWorkspaceDto>> Handle(
        SubmitFinalReviewCommand request,
        CancellationToken cancellationToken)
    {
        var access = await WorkspaceAccess.LoadByCodeAsync(
            workspaceRepository,
            taskRepository,
            currentUser,
            request.Code,
            includeArtifacts: true,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var workspace = access.Value!.Workspace;
        var task = access.Value.Task;
        if (workspace.Status == ArtifactWorkspaceStatus.Finalized)
        {
            return Result.Invalid("Workspace is already finalized.");
        }

        if (workspace.Artifacts.Count == 0)
        {
            return Result.Invalid("Workspace has no draft artifacts to submit for final review.");
        }

        if (task.Status is not AgentTaskStatus.WorkspaceReady and not AgentTaskStatus.WaitingFinalApproval)
        {
            return Result.Invalid("Only workspace-ready tasks can submit final review.");
        }

        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id),
            cancellationToken);
        var finalApproval = approvals.FirstOrDefault(item =>
            item.ApprovalType == AgentApprovalType.FinalOutput &&
            string.Equals(item.TargetId, workspace.WorkspaceCode, StringComparison.Ordinal));

        if (finalApproval?.Status == AgentApprovalStatus.Rejected)
        {
            return Result.Invalid("Workspace final output approval was rejected.");
        }

        if (finalApproval?.Status == AgentApprovalStatus.Approved)
        {
            return Result.Invalid("Workspace final output is already approved; call finalize to publish final artifacts.");
        }

        var now = DateTimeOffset.UtcNow;
        if (finalApproval is null)
        {
            finalApproval = new ApprovalRequest(
                task.Id,
                AgentApprovalType.FinalOutput,
                workspace.WorkspaceCode,
                currentUser.Id!.Value,
                now);
            approvalRepository.Add(finalApproval);
            await auditRecorder.RecordFinalReviewSubmittedAsync(task, workspace, finalApproval, cancellationToken);
        }

        if (task.Status == AgentTaskStatus.WorkspaceReady)
        {
            task.WaitForFinalApproval(now);
        }

        taskRepository.Update(task);
        workspaceRepository.Update(workspace);
        await workspaceRepository.SaveChangesAsync(cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        var files = await fileStore.ListAsync(workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(workspace, task, files));
    }
}

public sealed class FinalizeArtifactWorkspaceCommandHandler(
    IRepository<ArtifactWorkspace> workspaceRepository,
    IRepository<AgentTask> taskRepository,
    IRepository<AgentTaskRunAttempt> runAttemptRepository,
    IRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    AgentAuditRecorder auditRecorder,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService,
    CloudReadonlyProductionOperationsService? productionOperationsService = null)
    : ICommandHandler<FinalizeArtifactWorkspaceCommand, Result<ArtifactWorkspaceDto>>
{
    public async Task<Result<ArtifactWorkspaceDto>> Handle(
        FinalizeArtifactWorkspaceCommand request,
        CancellationToken cancellationToken)
    {
        var access = await WorkspaceAccess.LoadByCodeForOwnerOrPermissionAsync(
            workspaceRepository,
            taskRepository,
            currentUser,
            identityAccessService,
            request.Code,
            includeArtifacts: true,
            ownerPermission: AgentApprovalPermissions.FinalizeWorkspace,
            privilegedPermissions: [AgentApprovalPermissions.FinalizeWorkspace],
            approvalRepository: null,
            requireFinalOutputApprovalForPrivilegedAccess: false,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var workspace = access.Value!.Workspace;
        var task = access.Value.Task;
        if (workspace.Status == ArtifactWorkspaceStatus.Finalized)
        {
            var finalizedFiles = await fileStore.ListAsync(workspace.WorkspaceCode, cancellationToken);
            return Result.Success(ArtifactWorkspaceMapper.Map(workspace, task, finalizedFiles));
        }

        if (workspace.Artifacts.Count == 0)
        {
            return Result.Invalid("Workspace has no draft artifacts to finalize.");
        }

        var now = DateTimeOffset.UtcNow;
        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(task.Id),
            cancellationToken);
        var approval = approvals.FirstOrDefault(item =>
            item.ApprovalType == AgentApprovalType.FinalOutput &&
            string.Equals(item.TargetId, workspace.WorkspaceCode, StringComparison.Ordinal));
        if (approval is null)
        {
            return Result.Invalid("Final output approval is required before workspace finalization.");
        }

        if (approval.Status == AgentApprovalStatus.Pending)
        {
            return Result.Invalid("Final output approval is still pending.");
        }

        if (approval.Status == AgentApprovalStatus.Rejected)
        {
            return Result.Invalid("Workspace final output approval was rejected.");
        }

        if (approval.Status is AgentApprovalStatus.Cancelled or AgentApprovalStatus.Expired)
        {
            return Result.Invalid("Workspace final output approval is no longer valid.");
        }

        if (approval.Status != AgentApprovalStatus.Approved)
        {
            return Result.Invalid("Final output approval is not approved.");
        }

        foreach (var artifact in workspace.Artifacts.Where(item => item.Status != ArtifactStatus.Final))
        {
            if (artifact.Status is ArtifactStatus.Rejected or ArtifactStatus.Deleted)
            {
                return Result.Invalid($"Artifact {artifact.Name} cannot be finalized from status {artifact.Status}.");
            }

            if (artifact.Status is ArtifactStatus.Draft or ArtifactStatus.Reviewing)
            {
                artifact.Approve(now);
            }

            var currentPath = ArtifactPathGuard.NormalizeRelativePath(artifact.RelativePath);
            var finalPath = $"final/{currentPath}";
            await fileStore.CopyAsync(workspace.WorkspaceCode, currentPath, finalPath, artifact.MimeType, cancellationToken);
            artifact.MarkFinal(finalPath, now);
        }

        workspace.FinalizeWorkspace(now);
        if (task.Status == AgentTaskStatus.WorkspaceReady)
        {
            task.WaitForFinalApproval(now);
        }

        if (task.Status == AgentTaskStatus.WaitingFinalApproval)
        {
            task.MarkFinalized(now);
        }

        if (task.Status == AgentTaskStatus.Finalized)
        {
            task.Complete("产物已确认并输出到 final 目录。", now);
        }

        var backfillWarnings = productionOperationsService?.BackfillFinalArtifactRefs(
            task.Id.Value,
            workspace.Artifacts.Where(artifact => artifact.Status == ArtifactStatus.Final).ToArray()) ?? [];

        var activeRunAttemptId = task.ActiveRunAttemptId;
        var finalStep = task.Steps
            .OrderByDescending(step => step.StepIndex)
            .FirstOrDefault(step => string.Equals(step.ToolCode, "finalize_artifacts", StringComparison.OrdinalIgnoreCase));
        if (finalStep is not null &&
            finalStep.Status is AgentStepStatus.WaitingApproval or AgentStepStatus.Approved)
        {
            finalStep.Complete("""{"status":"finalized"}""", now);
        }

        if (activeRunAttemptId is not null)
        {
            var attempt = await runAttemptRepository.FirstOrDefaultAsync(
                new AgentTaskRunAttemptByIdSpec(activeRunAttemptId.Value),
                cancellationToken);
            if (attempt is not null && !attempt.IsTerminal)
            {
                attempt.MarkSucceeded(now, "Workspace final output approved.");
                runAttemptRepository.Update(attempt);
                task.ReleaseRunLease(now, clearActiveAttempt: true);
            }
        }

        workspaceRepository.Update(workspace);
        taskRepository.Update(task);
        await auditRecorder.RecordApprovalDecisionAsync(
            approval,
            task,
            AuditResults.Succeeded,
            "Workspace final output approved.",
            cancellationToken);
        await auditRecorder.RecordWorkspaceFinalizedAsync(
            task,
            workspace,
            AuditResults.Succeeded,
            backfillWarnings.Count == 0
                ? "Workspace artifacts finalized. Production Pilot ledger artifact refs backfilled when applicable."
                : $"Workspace artifacts finalized. Production Pilot ledger backfill warnings: {string.Join(" | ", backfillWarnings)}",
            cancellationToken);
        await workspaceRepository.SaveChangesAsync(cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        var files = await fileStore.ListAsync(workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(workspace, task, files));
    }
}

internal sealed record WorkspaceAccess(ArtifactWorkspace Workspace, AgentTask Task)
{
    public static async Task<Result<WorkspaceAccess>> LoadByCodeAsync(
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<AgentTask> taskRepository,
        ICurrentUser currentUser,
        string code,
        bool includeArtifacts,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Invalid("Workspace code is required.");
        }

        var workspace = await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByCodeSpec(code.Trim(), includeArtifacts),
            cancellationToken);
        if (workspace is null)
        {
            return Result.NotFound();
        }

        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdForUserSpec(workspace.TaskId, userId, includeSteps: true),
            cancellationToken);
        return task is null
            ? Result.NotFound()
            : Result.Success(new WorkspaceAccess(workspace, task));
    }

    public static async Task<Result<WorkspaceAccess>> LoadByCodeForOwnerOrPermissionAsync(
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        IReadRepository<AgentTask> taskRepository,
        ICurrentUser currentUser,
        IIdentityAccessService identityAccessService,
        string code,
        bool includeArtifacts,
        string ownerPermission,
        IReadOnlyCollection<string> privilegedPermissions,
        IReadRepository<ApprovalRequest>? approvalRepository,
        bool requireFinalOutputApprovalForPrivilegedAccess,
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

        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Invalid("Workspace code is required.");
        }

        var workspace = await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByCodeSpec(code.Trim(), includeArtifacts),
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

        var currentAccess = currentAccessResult.Value!;
        if (task.UserId == currentAccess.UserId)
        {
            return AgentApprovalPermissions.HasPermission(currentAccess, ownerPermission)
                ? Result.Success(new WorkspaceAccess(workspace, task))
                : AgentApprovalPermissions.ForbiddenMissing(ownerPermission);
        }

        if (!privilegedPermissions.Any(permission => AgentApprovalPermissions.HasPermission(currentAccess, permission)))
        {
            return Result.NotFound();
        }

        if (requireFinalOutputApprovalForPrivilegedAccess &&
            (approvalRepository is null ||
             !await HasFinalOutputApprovalAsync(approvalRepository, task.Id, workspace.WorkspaceCode, cancellationToken)))
        {
            return Result.NotFound();
        }

        return Result.Success(new WorkspaceAccess(workspace, task));
    }

    public static async Task<bool> HasFinalOutputApprovalAsync(
        IReadRepository<ApprovalRequest> approvalRepository,
        AgentTaskId taskId,
        string workspaceCode,
        CancellationToken cancellationToken)
    {
        var approvals = await approvalRepository.ListAsync(
            new ApprovalRequestsByTaskSpec(taskId),
            cancellationToken);
        return approvals.Any(item =>
            item.ApprovalType == AgentApprovalType.FinalOutput &&
            string.Equals(item.TargetId, workspaceCode, StringComparison.Ordinal));
    }
}
