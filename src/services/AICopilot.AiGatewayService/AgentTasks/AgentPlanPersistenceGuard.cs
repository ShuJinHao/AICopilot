using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentPlanPersistenceGuard
{
    public static async Task<Result<AgentPlanContractMetadata>> VerifyAsync(
        AgentTask task,
        IAgentTaskPlanPersistenceVerifier? persistenceVerifier,
        IAgentPlanIntegrityValidator? integrityValidator,
        bool requireExecutable,
        CancellationToken cancellationToken)
    {
        var persistedJson = task.PlanJson;
        if (persistenceVerifier is not null)
        {
            var fresh = await persistenceVerifier.VerifyFreshAsync(
                task.Id.Value,
                task.PlanJson,
                cancellationToken);
            if (!fresh.IsSuccess)
            {
                return Result.From(fresh);
            }

            persistedJson = fresh.Value!;
        }

        return (integrityValidator ?? new AgentPlanCanonicalizer())
            .ValidatePersisted(persistedJson, requireExecutable);
    }
}
