using System.Text;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Specifications.Approvals;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class ArtifactWorkspaceP9Coordinator(
    IRepository<ArtifactWorkspace> workspaceRepository,
    IRepository<AgentTask> taskRepository,
    IRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    AgentAuditRecorder auditRecorder,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
{
    public async Task<Result<AgentArtifactPreviewDto>> GetPreviewAsync(
        GetAgentArtifactPreviewQuery request,
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
        if (context.Artifact.Status is ArtifactStatus.Deleted or ArtifactStatus.Rejected)
        {
            return Result.Invalid($"Artifact status {context.Artifact.Status} cannot be previewed.");
        }

        var preview = await ArtifactPreviewBuilder.BuildAsync(
            fileStore,
            context.Workspace.WorkspaceCode,
            context.Artifact,
            cancellationToken);
        if (!preview.IsSuccess)
        {
            return Result.From(preview);
        }

        await auditRecorder.RecordArtifactPreviewedAsync(
            context.Task,
            context.Workspace,
            context.Artifact,
            preview.Value!.PreviewKind,
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return preview;
    }

    public async Task<Result<ArtifactRevisionCommentDto>> CreateRevisionCommentAsync(
        CreateArtifactRevisionCommentCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Comment))
        {
            return Result.Invalid("Revision comment is required.");
        }

        var access = await ArtifactVersioningAccess.LoadArtifactForOwnerEditAsync(
            workspaceRepository,
            taskRepository,
            currentUser,
            identityAccessService,
            request.Id,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var validation = await ArtifactWorkspaceP9Policy.ValidateDraftMutationAsync(
            approvalRepository,
            access.Value!,
            request.ExpectedVersion,
            allowBinaryArtifact: true,
            cancellationToken);
        if (!validation.IsSuccess)
        {
            return Result.From(validation);
        }

        var commentHash = ArtifactWorkspaceP9Policy.ComputeHash(request.Comment);
        await auditRecorder.RecordArtifactRevisionCommentAsync(
            access.Value!.Task,
            access.Value.Workspace,
            access.Value.Artifact,
            commentHash,
            cancellationToken);
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        return Result.Success(new ArtifactRevisionCommentDto(
            request.Id,
            access.Value.Artifact.Version,
            commentHash,
            DateTimeOffset.UtcNow));
    }

    public async Task<Result<ArtifactWorkspaceDto>> RegenerateDraftAsync(
        RegenerateDraftArtifactCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            return Result.Invalid("Regenerated artifact content is required.");
        }

        if (Encoding.UTF8.GetByteCount(request.Content) > ArtifactVersioningPolicy.MaxContentBytes)
        {
            return Result.Invalid($"Regenerated artifact content exceeds the {ArtifactVersioningPolicy.MaxContentBytes} byte limit.");
        }

        var access = await ArtifactVersioningAccess.LoadArtifactForOwnerEditAsync(
            workspaceRepository,
            taskRepository,
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
        await auditLogWriter.SaveChangesAsync(cancellationToken);

        var files = await fileStore.ListAsync(context.Workspace.WorkspaceCode, cancellationToken);
        return Result.Success(ArtifactWorkspaceMapper.Map(context.Workspace, context.Task, files));
    }

    public async Task<Result<ArtifactWorkspaceDto>> SubmitForFinalApprovalAsync(
        SubmitArtifactForFinalApprovalCommand request,
        CancellationToken cancellationToken)
    {
        var access = await ArtifactVersioningAccess.LoadArtifactForReadAsync(
            workspaceRepository,
            taskRepository,
            approvalRepository,
            currentUser,
            identityAccessService,
            request.Id,
            AgentApprovalPermissions.SubmitFinalReview,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var context = access.Value!;
        if (!context.IsOwner)
        {
            return Result.NotFound();
        }

        var workspace = context.Workspace;
        var task = context.Task;
        if (workspace.Status == ArtifactWorkspaceStatus.Finalized)
        {
            return Result.Invalid("Workspace is already finalized.");
        }

        if (context.Artifact.Status == ArtifactStatus.Final)
        {
            return Result.Invalid("Artifact is already final.");
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
