using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AICopilot.EntityFrameworkCore.Repository;

internal sealed class AgentTaskPlanPersistenceVerifier(
    IServiceScopeFactory scopeFactory) : IAgentTaskPlanPersistenceVerifier
{
    public async Task<Result<string>> VerifyFreshAsync(
        Guid taskId,
        string expectedCanonicalPlanJson,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AiGatewayDbContext>();
        var id = new AgentTaskId(taskId);
        var persisted = await dbContext.AgentTasks
            .AsNoTracking()
            .Where(task => task.Id == id)
            .Select(task => task.PlanJson)
            .SingleOrDefaultAsync(cancellationToken);
        if (persisted is null)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                "Fresh-context Plan v2 verification could not reload the persisted task."));
        }

        if (!string.Equals(persisted, expectedCanonicalPlanJson, StringComparison.Ordinal))
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                "Fresh-context Plan v2 bytes differ from the sealed canonical payload."));
        }

        return Result.Success(persisted);
    }
}
