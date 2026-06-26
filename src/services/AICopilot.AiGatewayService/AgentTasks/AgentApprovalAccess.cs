using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Core.AiGateway.Specifications.Artifacts;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentApprovalAccess
{
    public static Result MissingUser()
    {
        return Result.Unauthorized(new ApiProblemDescriptor(
            AuthProblemCodes.Unauthorized,
            "Current user id is missing or invalid."));
    }

    public static async Task<Result<AgentTask>> LoadTaskAsync(
        IRepository<AgentTask> taskRepository,
        ICurrentUser currentUser,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return MissingUser();
        }

        if (taskId == Guid.Empty)
        {
            return Result.Invalid("Agent task id is required.");
        }

        var task = await taskRepository.FirstOrDefaultAsync(
            new AgentTaskByIdForUserSpec(new AgentTaskId(taskId), userId, includeSteps: true),
            cancellationToken);
        return task is null ? Result.NotFound() : Result.Success(task);
    }

    public static async Task<ArtifactWorkspace?> LoadWorkspaceAsync(
        IReadRepository<ArtifactWorkspace> workspaceRepository,
        AgentTask task,
        CancellationToken cancellationToken)
    {
        if (task.WorkspaceId is null)
        {
            return null;
        }

        return await workspaceRepository.FirstOrDefaultAsync(
            new ArtifactWorkspaceByIdSpec(task.WorkspaceId.Value, includeArtifacts: true),
            cancellationToken);
    }
}
