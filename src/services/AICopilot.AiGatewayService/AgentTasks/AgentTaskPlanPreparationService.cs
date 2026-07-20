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
    IBusinessDatabaseReadService? businessDatabaseReadService)
{
    public async Task<Result<AgentTaskPlanPreparation>> PrepareAsync(
        PlanAgentTaskCommand request,
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken)
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
            .ToArray();
        BusinessDatabaseDescriptor[] selectedDataSources = [];
        if (dataSourceIds.Length > 0)
        {
            if (businessDatabaseReadService is null)
            {
                return Result.Failure("Business database read service is not configured.");
            }

            var visibleDataSources = await businessDatabaseReadService.ListSelectableAsync(
                DataSourceSelectionMode.Agent,
                cancellationToken);
            selectedDataSources = visibleDataSources
                .Where(source => source.IsSelectableInAgent)
                .Where(source => dataSourceIds.Contains(source.Id))
                .ToArray();
            if (selectedDataSources.Length != dataSourceIds.Length)
            {
                return Result.NotFound();
            }

            if (selectedDataSources.Any(source => source.ExternalSystemType is not (
                    DataSourceExternalSystemType.SimulationBusiness or
                    DataSourceExternalSystemType.CloudReadOnly)))
            {
                return Result.Invalid(
                    "Governed agent data tasks require an explicitly authorized SimulationBusiness or CloudReadOnly data source.");
            }
        }

        var businessDomains = (request.BusinessDomains ?? [])
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var isSimulationOnlyPlan = selectedDataSources.Any(source =>
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
}
