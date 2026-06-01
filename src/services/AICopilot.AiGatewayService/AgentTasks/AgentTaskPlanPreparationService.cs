using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Tools;
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
    CloudReadonlySandboxControlledTrialService? cloudSandboxControlledTrialService,
    CloudReadonlyProductionControlledPilotService? cloudProductionControlledPilotService,
    CloudReadonlyProductionPilotService? cloudReadonlyProductionPilotService,
    CloudReadonlyPilotReadinessService? cloudReadonlyPilotReadinessService,
    IReadRepository<ToolRegistration>? toolReadRepository)
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

        var isCloudSandboxFixedTrialPlan = request.IsCloudSandboxTrial ||
                                           CloudReadonlySandboxAgentTrialService.IsScenarioId(request.TrialScenarioId);
        var isCloudSandboxControlledTrialPlan = request.IsCloudSandboxControlledTrial ||
                                                request.CloudSandboxGoalIntent is not null;
        var isCloudSandboxTrialPlan = isCloudSandboxFixedTrialPlan || isCloudSandboxControlledTrialPlan;
        var isCloudProductionPilotTrialPlan = request.IsCloudProductionPilotTrial ||
                                             CloudReadonlyProductionPilotService.IsScenarioId(request.TrialScenarioId);
        var isCloudProductionControlledPilotPlan = request.IsCloudProductionControlledPilotTrial ||
                                                   request.CloudProductionGoalIntent is not null;
        if (isCloudSandboxFixedTrialPlan && !CloudReadonlySandboxAgentTrialService.IsScenarioId(request.TrialScenarioId))
        {
            return Result.Invalid("P7 CloudReadonlySandbox agent trial only allows fixed trial scenarios.");
        }

        if (isCloudProductionPilotTrialPlan && !CloudReadonlyProductionPilotService.IsScenarioId(request.TrialScenarioId))
        {
            return Result.Invalid("P12 CloudReadonlyProductionPilot only allows fixed Pilot scenarios.");
        }

        if ((isCloudProductionPilotTrialPlan || isCloudProductionControlledPilotPlan) && isCloudSandboxTrialPlan)
        {
            return Result.Invalid("CloudReadonlySandbox and CloudReadonlyProductionPilot scenarios cannot be mixed in one plan.");
        }

        if (isCloudProductionPilotTrialPlan && isCloudProductionControlledPilotPlan)
        {
            return Result.Invalid("P12 fixed production Pilot and P13 controlled production Pilot cannot be mixed in one plan.");
        }

        if (isCloudSandboxFixedTrialPlan && isCloudSandboxControlledTrialPlan)
        {
            return Result.Invalid("CloudReadonlySandbox fixed scenarios and controlled goals cannot be mixed in one plan.");
        }

        if (isCloudProductionControlledPilotPlan)
        {
            if (cloudProductionControlledPilotService is null ||
                cloudReadonlyProductionPilotService is null ||
                cloudReadonlyPilotReadinessService is null ||
                toolReadRepository is null)
            {
                return Result.Failure("CloudReadonlyProductionControlledPilot services are not configured.");
            }

            var protectedToolsForControlledPilot = await GetCloudReadonlyPilotReadinessQueryHandler.LoadProtectedToolRegistrationsAsync(
                toolReadRepository,
                cancellationToken);
            var p12StatusForControlledPilot = cloudReadonlyProductionPilotService.BuildStatus(
                cloudReadonlyPilotReadinessService.BuildStatus(protectedToolsForControlledPilot),
                protectedToolsForControlledPilot);
            var intentValidation = cloudProductionControlledPilotService.ValidateIntentForPlan(
                request.CloudProductionGoalIntent,
                p12StatusForControlledPilot,
                protectedToolsForControlledPilot);
            if (!intentValidation.IsSuccess)
            {
                return Result.From(intentValidation);
            }
        }

        if (isCloudSandboxControlledTrialPlan)
        {
            if (cloudSandboxControlledTrialService is null)
            {
                return Result.Failure("CloudReadonlySandbox controlled trial service is not configured.");
            }

            var intentValidation = cloudSandboxControlledTrialService.ValidateIntentForPlan(request.CloudSandboxGoalIntent);
            if (!intentValidation.IsSuccess)
            {
                return Result.From(intentValidation);
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

            if (isCloudSandboxTrialPlan || isCloudProductionPilotTrialPlan || isCloudProductionControlledPilotPlan)
            {
                return Result.Invalid("CloudReadonly agent trial cannot bind BusinessDatabase data sources.");
            }

            if (selectedDataSources.Any(source => source.ExternalSystemType != DataSourceExternalSystemType.SimulationBusiness))
            {
                return Result.Invalid("P3 dynamic planner data tasks can only use SimulationBusiness data sources.");
            }
        }

        var businessDomains = (request.BusinessDomains ?? [])
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (isCloudSandboxTrialPlan && businessDomains.Length == 0)
        {
            if (isCloudSandboxControlledTrialPlan)
            {
                businessDomains = request.CloudSandboxGoalIntent?.EndpointCodes.ToArray() ?? [];
            }
            else
            {
                var trialDomain = CloudReadonlySandboxAgentTrialService.ResolveScenarioDomain(request.TrialScenarioId);
                businessDomains = string.IsNullOrWhiteSpace(trialDomain) ? [] : [trialDomain];
            }
        }
        else if (isCloudProductionPilotTrialPlan && businessDomains.Length == 0)
        {
            var trialDomain = CloudReadonlyProductionPilotService.ResolveScenarioDomain(request.TrialScenarioId);
            businessDomains = string.IsNullOrWhiteSpace(trialDomain) ? [] : [trialDomain];
        }
        else if (isCloudProductionControlledPilotPlan && businessDomains.Length == 0)
        {
            businessDomains = request.CloudProductionGoalIntent?.EndpointCodes.ToArray() ?? [];
        }

        var isSimulationOnlyPlan = request.IsSimulationTrial ||
                                   selectedDataSources.Any(source =>
                                       source.ExternalSystemType == DataSourceExternalSystemType.SimulationBusiness);
        var hasBusinessDataSourcesForPlan = !isCloudSandboxTrialPlan &&
                                            !isCloudProductionPilotTrialPlan &&
                                            !isCloudProductionControlledPilotPlan &&
                                            (dataSourceIds.Length > 0 || businessDomains.Length > 0);

        return Result.Success(new AgentTaskPlanPreparation(
            userId,
            uploadIds,
            knowledgeBaseIds,
            dataSourceIds,
            selectedDataSources,
            businessDomains,
            isCloudSandboxFixedTrialPlan,
            isCloudSandboxControlledTrialPlan,
            isCloudSandboxTrialPlan,
            isCloudProductionPilotTrialPlan,
            isCloudProductionControlledPilotPlan,
            isSimulationOnlyPlan,
            hasBusinessDataSourcesForPlan));
    }
}
