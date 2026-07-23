using AICopilot.AiGatewayService.Agents;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal interface IAgentPlanRuntimeSnapshotVerifier
{
    Task<Result> VerifyAsync(
        AgentTaskPlanDocument plan,
        Guid userId,
        CancellationToken cancellationToken);
}

internal sealed class AgentPlanRuntimeSnapshotVerifier(
    AgentPlanToolGuard planToolGuard,
    IAgentRoutingConfigurationSnapshotReader routingSnapshotReader,
    IAgentPlanAuthorizationFreshVerifier? authorizationFreshVerifier = null,
    ConfiguredAgentRuntimeFactory? reasoningConfigurationFactory = null)
    : IAgentPlanRuntimeSnapshotVerifier
{
    public async Task<Result> VerifyAsync(
        AgentTaskPlanDocument plan,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (authorizationFreshVerifier is null &&
            (plan.KnowledgeBaseIds.Count > 0 || plan.DataSourceIds is { Count: > 0 }))
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.ApprovalReconfirmationRequired,
                "Runtime knowledge/governed-data authorization verification is unavailable; generate and confirm a new PlanDraft."));
        }

        if (authorizationFreshVerifier is not null)
        {
            var authorization = await authorizationFreshVerifier.VerifyAsync(
                plan.KnowledgeBaseIds,
                plan.DataSourceIds ?? [],
                userId,
                cancellationToken);
            if (!authorization.IsSuccess)
            {
                return Result.From(authorization);
            }
        }

        var currentCatalog = await planToolGuard.GetAvailableToolCatalogAsync(
            userId,
            plan.PlannerSafetySummary?.IsSimulationOnly ?? false,
            plan.BusinessDomains,
            cancellationToken,
            pluginSelectionMode: plan.PluginSelectionMode);
        if (!currentCatalog.IsSuccess)
        {
            return Result.From(currentCatalog);
        }

        RuntimeAgentConfigurationSnapshot currentRoutingConfiguration;
        try
        {
            currentRoutingConfiguration = await routingSnapshotReader.ReadCurrentAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.ApprovalReconfirmationRequired,
                "Runtime routing prompt/model configuration is unavailable; generate and confirm a new PlanDraft."));
        }

        if (plan.ExecutionSnapshot is null ||
            !AgentPlanCatalogSnapshotAuthority.Matches(
                plan.ExecutionSnapshot,
                currentCatalog.Value!,
                currentRoutingConfiguration,
                plan.DataSourceIds,
                plan.KnowledgeBaseIds,
                plan.IntentCandidates ?? [],
                plan.ConcurrencyPolicy?.PolicyVersion))
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.ApprovalReconfirmationRequired,
                "Runtime tool/provider/schema/prompt/model snapshot changed after confirmation; generate and confirm a new PlanDraft."));
        }

        var reasoningConfiguration = await AgentReasoningPolicyAuthority.VerifyCurrentConfigurationAsync(
            plan,
            reasoningConfigurationFactory,
            cancellationToken);
        if (!reasoningConfiguration.IsSuccess)
        {
            return Result.From(reasoningConfiguration);
        }

        return Result.Success();
    }
}
