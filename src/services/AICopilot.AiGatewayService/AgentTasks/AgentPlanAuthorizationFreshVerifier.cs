using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public interface IAgentPlanAuthorizationFreshVerifier
{
    Task<Result> VerifyAsync(
        IReadOnlyCollection<Guid> knowledgeBaseIds,
        IReadOnlyCollection<Guid> dataSourceIds,
        Guid userId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Re-reads the narrow authorization rosters that Plan v2 can persist. Tool and
/// permission state is independently re-read by AgentPlanToolGuard and compared
/// through ToolCatalogDigest; this verifier owns knowledge/data resource access.
/// </summary>
internal sealed class AgentPlanAuthorizationFreshVerifier(
    IEnumerable<IKnowledgeBaseAccessChecker> knowledgeBaseAccessCheckers,
    IIdentityAccessService? identityAccessService = null,
    IBusinessDatabaseAuthorizationReadService? businessDatabaseAuthorizationReadService = null)
    : IAgentPlanAuthorizationFreshVerifier
{
    public async Task<Result> VerifyAsync(
        IReadOnlyCollection<Guid> knowledgeBaseIds,
        IReadOnlyCollection<Guid> dataSourceIds,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (knowledgeBaseIds.Count > 0)
        {
            var checker = knowledgeBaseAccessCheckers.FirstOrDefault();
            var access = identityAccessService is null
                ? null
                : await identityAccessService.GetCurrentUserAccessAsync(userId, cancellationToken);
            if (checker is null || access is null)
            {
                return ReconfirmationRequired();
            }

            var isAdmin = string.Equals(access.RoleName, "Admin", StringComparison.OrdinalIgnoreCase);
            foreach (var knowledgeBaseId in knowledgeBaseIds)
            {
                if (!await checker.CanReadAsync(knowledgeBaseId, userId, isAdmin, cancellationToken))
                {
                    return ReconfirmationRequired();
                }
            }
        }

        if (dataSourceIds.Count > 0)
        {
            if (businessDatabaseAuthorizationReadService is null)
            {
                return ReconfirmationRequired();
            }

            var current = await businessDatabaseAuthorizationReadService.ListSelectableForUserAsync(
                userId,
                DataSourceSelectionMode.Agent,
                cancellationToken);
            var authorizedIds = current
                .Where(source => source.IsEnabled &&
                                 source.IsReadOnly &&
                                 source.IsSelectableInAgent)
                .Select(source => source.Id)
                .ToHashSet();
            if (dataSourceIds.Any(id => !authorizedIds.Contains(id)))
            {
                return ReconfirmationRequired();
            }
        }

        return Result.Success();
    }

    private static Result ReconfirmationRequired()
    {
        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.ApprovalReconfirmationRequired,
            "Knowledge or governed-data authorization changed after PlanDraft sealing; generate and confirm a new PlanDraft."));
    }
}
