using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentTaskCommandLoader
{
    public static async Task<Result<AgentTask>> LoadTaskAsync(
        IRepository<AgentTask> repository,
        ICurrentUser currentUser,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        if (id == Guid.Empty)
        {
            return Result.Invalid("Agent task id is required.");
        }

        var task = await repository.FirstOrDefaultAsync(
            new AgentTaskByIdForUserSpec(new AgentTaskId(id), userId, includeSteps: true),
            cancellationToken);
        return task is null ? Result.NotFound() : Result.Success(task);
    }
}
