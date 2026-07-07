using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workspaces;

public sealed class ArtifactWorkspaceQueryCoordinator(
    IReadRepository<ArtifactWorkspace> workspaceRepository,
    IReadRepository<AgentTask> taskRepository,
    IReadRepository<ApprovalRequest> approvalRepository,
    IArtifactWorkspaceFileStore fileStore,
    AgentAuditRecorder auditRecorder,
    IAuditLogWriter auditLogWriter,
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
{
    public async Task<Result<ArtifactWorkspaceDto>> GetAsync(
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

    public async Task<Result<ArtifactDownloadDto>> DownloadAsync(
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
