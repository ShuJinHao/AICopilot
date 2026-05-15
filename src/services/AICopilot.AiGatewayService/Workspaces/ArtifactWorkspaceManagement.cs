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
    DateTimeOffset? FinalizedAt);

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
    IReadOnlyCollection<ArtifactDto> Artifacts);

public sealed record ArtifactDownloadDto(Stream Stream, string FileName, string MimeType, long FileSize);

public sealed record ArtifactWorkspaceSettingsDto(
    string RootPath,
    IReadOnlyCollection<string> Folders,
    IReadOnlyCollection<string> AllowedArtifactTypes,
    bool AllowsUserDefinedPath);

[AuthorizeRequirement("AiGateway.GetWorkspace")]
public sealed record GetArtifactWorkspaceQuery(string Code) : IQuery<Result<ArtifactWorkspaceDto>>;

[AuthorizeRequirement("AiGateway.DownloadArtifact")]
public sealed record DownloadArtifactQuery(Guid Id) : IQuery<Result<ArtifactDownloadDto>>;

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
        CancellationToken cancellationToken);

    Task<Artifact> WriteDraftBinaryArtifactAsync(
        ArtifactWorkspace workspace,
        ArtifactType artifactType,
        string name,
        string relativePath,
        byte[] content,
        string mimeType,
        AgentStepId? stepId,
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
        CancellationToken cancellationToken)
    {
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
        CancellationToken cancellationToken)
    {
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
        workspaceRepository.Update(workspace);
        return artifact;
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
            workspace.Artifacts
                .OrderBy(artifact => artifact.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(artifact => new ArtifactDto(
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
                    artifact.CreatedByStepId.HasValue && stepIndexes.TryGetValue(artifact.CreatedByStepId.Value, out var stepIndex)
                        ? stepIndex
                        : null,
                    artifact.Status != ArtifactStatus.Final,
                    ResolveApprovalStatus(artifact),
                    artifact.Status == ArtifactStatus.Final ? artifact.UpdatedAt : null))
                .ToArray());
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
    IArtifactWorkspaceFileStore fileStore,
    ICurrentUser currentUser)
    : IQueryHandler<GetArtifactWorkspaceQuery, Result<ArtifactWorkspaceDto>>
{
    public async Task<Result<ArtifactWorkspaceDto>> Handle(
        GetArtifactWorkspaceQuery request,
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

        var files = await fileStore.ListAsync(access.Value!.Workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(access.Value.Workspace, access.Value.Task, files));
    }
}

public sealed class DownloadArtifactQueryHandler(
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<AgentTask> taskRepository,
    IArtifactWorkspaceFileStore fileStore,
    AgentAuditRecorder auditRecorder,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser)
    : IQueryHandler<DownloadArtifactQuery, Result<ArtifactDownloadDto>>
{
    public async Task<Result<ArtifactDownloadDto>> Handle(
        DownloadArtifactQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        var workspace = await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByArtifactIdSpec(new ArtifactId(request.Id)),
            cancellationToken);
        if (workspace is null)
        {
            return Result.NotFound();
        }

        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdForUserSpec(workspace.TaskId, userId),
            cancellationToken);
        if (task is null)
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

public sealed class FinalizeArtifactWorkspaceCommandHandler(
    IRepository<ArtifactWorkspace> workspaceRepository,
    IRepository<AgentTask> taskRepository,
    IRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    AgentAuditRecorder auditRecorder,
    ICurrentUser currentUser)
    : ICommandHandler<FinalizeArtifactWorkspaceCommand, Result<ArtifactWorkspaceDto>>
{
    public async Task<Result<ArtifactWorkspaceDto>> Handle(
        FinalizeArtifactWorkspaceCommand request,
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
        var userId = currentUser.Id!.Value;
        if (workspace.Artifacts.Count == 0)
        {
            return Result.Invalid("Workspace has no draft artifacts to finalize.");
        }

        var now = DateTimeOffset.UtcNow;
        var approval = await approvalRepository.FirstOrDefaultAsync(
            new PendingApprovalRequestByTaskAndTargetSpec(
                task.Id,
                AgentApprovalType.FinalOutput,
                workspace.WorkspaceCode),
            cancellationToken);
        if (approval is null)
        {
            var existingFinalApprovals = await approvalRepository.ListAsync(
                new ApprovalRequestsByTaskSpec(task.Id),
                cancellationToken);
            var decidedApproval = existingFinalApprovals.FirstOrDefault(item =>
                item.ApprovalType == AgentApprovalType.FinalOutput &&
                item.TargetId == workspace.WorkspaceCode &&
                item.Status is AgentApprovalStatus.Approved or AgentApprovalStatus.Rejected);
            if (decidedApproval?.Status == AgentApprovalStatus.Rejected)
            {
                return Result.Invalid("Workspace final output approval was rejected.");
            }

            approval = decidedApproval ?? new ApprovalRequest(
                task.Id,
                AgentApprovalType.FinalOutput,
                workspace.WorkspaceCode,
                task.UserId,
                now);
            if (decidedApproval is null)
            {
                approvalRepository.Add(approval);
            }
        }

        if (approval.Status == AgentApprovalStatus.Pending)
        {
            approval.Approve(userId, "Workspace final output confirmed.", now);
        }
        foreach (var artifact in workspace.Artifacts.Where(item => item.Status != ArtifactStatus.Final))
        {
            if (artifact.Status is ArtifactStatus.Draft or ArtifactStatus.Reviewing)
            {
                artifact.Approve(now);
            }

            var finalPath = $"final/{Path.GetFileName(artifact.RelativePath)}";
            var currentPath = artifact.RelativePath;
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
            task.Complete("产物已确认并输出到 final 目录。", now);
        }

        var finalStep = task.Steps
            .OrderByDescending(step => step.StepIndex)
            .FirstOrDefault(step => string.Equals(step.ToolCode, "finalize_artifacts", StringComparison.OrdinalIgnoreCase));
        if (finalStep is not null && finalStep.Status == AgentStepStatus.WaitingApproval)
        {
            finalStep.Complete("""{"status":"finalized"}""", now);
        }

        approvalRepository.Update(approval);
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
            "Workspace artifacts finalized.",
            cancellationToken);
        await workspaceRepository.SaveChangesAsync(cancellationToken);

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
}
