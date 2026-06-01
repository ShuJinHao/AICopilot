using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class GetAgentArtifactPreviewQueryHandler(
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<AgentTask> taskRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    AgentAuditRecorder auditRecorder,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : IQueryHandler<GetAgentArtifactPreviewQuery, Result<AgentArtifactPreviewDto>>
{
    public async Task<Result<AgentArtifactPreviewDto>> Handle(
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
}
