using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.Sessions;
using AICopilot.Core.AiGateway.Specifications.Uploads;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentTaskPlanPreparationService(
    IReadRepository<Session> sessionRepository,
    IReadRepository<UploadRecord> uploadRepository,
    IEnumerable<IKnowledgeBaseAccessChecker> knowledgeBaseAccessCheckers,
    IBusinessDatabaseReadService? businessDatabaseReadService,
    IBusinessDatabaseAuthorizationReadService? businessDatabaseAuthorizationReadService = null,
    IBusinessDataSourceProfileRegistry? businessDataSourceProfileRegistry = null)
{
    public async Task<Result<AgentTaskPlanPreparation>> PrepareAsync(
        PlanAgentTaskCommand request,
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken,
        bool useDevelopmentSimulationProfile = false)
    {
        if (request.SessionId == Guid.Empty)
        {
            return Result.Invalid("SessionId is required.");
        }

        var session = await sessionRepository.FirstOrDefaultAsync(
            new SessionByIdForUserSpec(new SessionId(request.SessionId), userId),
            cancellationToken);
        if (session is null)
        {
            return Result.NotFound("Session not found.");
        }

        var uploadIds = (request.UploadIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (uploadIds.Length > 0)
        {
            var uploads = await uploadRepository.ListAsync(
                new UploadRecordsByIdsForUserSpec(uploadIds.Select(id => new UploadRecordId(id)).ToArray(), userId),
                cancellationToken);
            if (uploads.Count != uploadIds.Length)
            {
                return Result.Invalid("One or more upload records do not exist or are not visible to current user.");
            }
        }

        var knowledgeBaseIds = (request.KnowledgeBaseIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (knowledgeBaseIds.Length > 0)
        {
            var accessChecker = knowledgeBaseAccessCheckers.FirstOrDefault();
            if (accessChecker is null)
            {
                return Result.Failure("RAG knowledge base access checker is not configured.");
            }

            foreach (var knowledgeBaseId in knowledgeBaseIds)
            {
                var canRead = await accessChecker.CanReadAsync(
                    knowledgeBaseId,
                    userId,
                    isAdmin,
                    cancellationToken);
                if (!canRead)
                {
                    return Result.NotFound();
                }
            }
        }

        var dataSourceIds = (request.DataSourceIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .OrderBy(id => id.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        BusinessDatabaseDescriptor[] selectedDataSources = [];
        if (useDevelopmentSimulationProfile && dataSourceIds.Length == 0)
        {
            return Result.Invalid(
                "The Development Simulation profile does not select a SimulationBusiness data source; DataSourceIds must explicitly name one authorized source.");
        }

        if (dataSourceIds.Length > 0)
        {
            if (businessDatabaseReadService is null)
            {
                return Result.Failure("Business database read service is not configured.");
            }

            if (useDevelopmentSimulationProfile && businessDatabaseAuthorizationReadService is null)
            {
                return Result.Invalid(
                    "The Development Simulation profile requires user-scoped data-source authorization; no global-source fallback is allowed.");
            }

            var visibleDataSources = useDevelopmentSimulationProfile
                ? await businessDatabaseAuthorizationReadService!.ListSelectableForUserAsync(
                    userId,
                    DataSourceSelectionMode.Agent,
                    cancellationToken)
                : await businessDatabaseReadService.ListSelectableAsync(
                    DataSourceSelectionMode.Agent,
                    cancellationToken);
            var selectableDataSources = visibleDataSources
                .Where(source => source.IsSelectableInAgent)
                .OrderBy(source => source.Id.ToString("D"), StringComparer.Ordinal)
                .ToArray();

            selectedDataSources = selectableDataSources
                .Where(source => dataSourceIds.Contains(source.Id))
                .ToArray();

            if (selectedDataSources.Length != dataSourceIds.Length)
            {
                return Result.NotFound();
            }

            if (businessDataSourceProfileRegistry is null)
            {
                return Result.Failure(
                    "Business data-source profile registry is not configured.");
            }

            if (selectedDataSources.Any(source =>
                    !businessDataSourceProfileRegistry.TryGet(
                        ResolveProfileKey(source),
                        source.ExternalSystemType,
                        out _)))
            {
                return Result.Invalid(
                    "Governed agent data tasks require an explicitly authorized source with a registered profile.");
            }

            var containsSimulation = selectedDataSources.Any(source =>
                source.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness);
            if (containsSimulation && selectedDataSources.Any(source =>
                    source.ExternalSystemType != DataSourceExternalSystemType.SimulationBusiness))
            {
                return Result.Invalid(
                    "SimulationBusiness and non-simulation data sources cannot be mixed in one Agent plan.");
            }

            if (containsSimulation && !useDevelopmentSimulationProfile)
            {
                return Result.Invalid(
                    "SimulationBusiness execution requires the explicit Development Simulation profile.");
            }

            if (useDevelopmentSimulationProfile && selectedDataSources.Any(source =>
                    source.ExternalSystemType != DataSourceExternalSystemType.SimulationBusiness))
            {
                return Result.Invalid(
                    "The Development Simulation profile accepts SimulationBusiness only; it never falls back to Real or Cloud data.");
            }
        }

        var businessDomains = useDevelopmentSimulationProfile && selectedDataSources.Length > 0
            ? selectedDataSources
                .Select(source => source.BusinessDomain)
                .Where(domain => !string.IsNullOrWhiteSpace(domain))
                .Select(domain => domain.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(domain => domain, StringComparer.Ordinal)
                .ToArray()
            : (request.BusinessDomains ?? [])
                .Where(domain => !string.IsNullOrWhiteSpace(domain))
                .Select(domain => domain.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(domain => domain, StringComparer.Ordinal)
                .ToArray();

        var isSimulationOnlyPlan = selectedDataSources.Length > 0 && selectedDataSources.All(source =>
            source.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness);
        var hasBusinessDataSourcesForPlan = dataSourceIds.Length > 0 || businessDomains.Length > 0;

        return Result.Success(new AgentTaskPlanPreparation(
            userId,
            uploadIds,
            knowledgeBaseIds,
            dataSourceIds,
            selectedDataSources,
            businessDomains,
            isSimulationOnlyPlan,
            hasBusinessDataSourcesForPlan));
    }

    private static string ResolveProfileKey(BusinessDatabaseDescriptor source)
    {
        return BusinessDataSourceProfileKeyResolver.Resolve(source);
    }
}
