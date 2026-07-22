using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class AgentPlanDraftConfirmationService(
    AgentPlanToolGuard planToolGuard,
    AgentTaskPlanFreshReadGate freshReadGate,
    IAgentRoutingConfigurationSnapshotReader routingSnapshotReader,
    ICloudReadonlyAgentPlanService? legacyCloudReadonlyPlanService = null,
    IAgentPlanIntegrityValidator? planIntegrityValidator = null,
    AgentPlanDraftContractAuthority? planContractAuthority = null,
    IAgentPlanAuthorizationFreshVerifier? authorizationFreshVerifier = null)
{
    public async Task<Result> ConfirmAsync(
        AgentTask task,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Cloud semantic intent is resolved while the PlanDraft is created. Confirmation
        // only validates the sealed snapshot and must not reinterpret the user's goal.
        _ = legacyCloudReadonlyPlanService;
        var planResult = DeserializePlan(task.PlanJson);
        if (!planResult.IsSuccess)
        {
            return Result.From(planResult);
        }

        var freshIntegrity = await freshReadGate.VerifyAsync(
            task,
            requireExecutable: false,
            cancellationToken);
        if (!freshIntegrity.IsSuccess)
        {
            return Result.From(freshIntegrity);
        }

        var integrityValidator = planIntegrityValidator ?? new AgentPlanCanonicalizer();
        var plan = planResult.Value!;
        if (!string.Equals(plan.PlanKind, AgentTaskPlanKinds.PlanDraft, StringComparison.Ordinal) ||
            plan.IsExecutable)
        {
            return InvalidPlan("Only a sealed non-executable PlanDraft v2 can be confirmed.");
        }

        if (plan.CapabilityGaps is { Count: > 0 })
        {
            return InvalidPlan(
                "A PlanDraft with unresolved capability gaps cannot be confirmed.");
        }

        if (plan.Nodes is not { Count: > 0 })
        {
            return InvalidPlan(
                "A gap-free PlanDraft requires a non-empty authoritative LinearV1 compiler graph before confirmation.");
        }

        if (plan.Steps.Count == 0 || plan.RequestedCapabilityCodes is null || plan.RequestedCapabilityCodes.Count == 0)
        {
            return InvalidPlan("A PlanDraft without steps and resolved capabilities cannot be confirmed.");
        }

        var steps = plan.Steps
            .Select(step => new AgentStepPlanDto(
                step.Title,
                step.Description,
                step.StepType,
                step.ToolCode,
                step.RequiresApproval,
                step.InputJson))
            .ToArray();
        var simulationOnly = plan.PlannerSafetySummary?.IsSimulationOnly ?? false;
        var guardedStepsResult = await planToolGuard.ValidateStepsAsync(
            steps,
            task.TaskType,
            task.UserId,
            simulationOnly,
            plan.BusinessDomains,
            cancellationToken,
            plan.SkillCode,
            plan.PluginSelectionMode);
        if (!guardedStepsResult.IsSuccess)
        {
            return Result.From(guardedStepsResult);
        }

        var guardedSteps = guardedStepsResult.Value!.ToArray();
        if (!StepsMatch(plan.Steps, guardedSteps))
        {
            return ReconfirmationRequired(
                "Tool approval or input policy changed after PlanDraft sealing; generate and confirm a new PlanDraft.");
        }

        if (authorizationFreshVerifier is null &&
            (plan.KnowledgeBaseIds.Count > 0 || plan.DataSourceIds is { Count: > 0 }))
        {
            return ReconfirmationRequired(
                "Fresh knowledge/governed-data authorization verification is unavailable; generate and confirm a new PlanDraft after authorization services are restored.");
        }

        if (authorizationFreshVerifier is not null)
        {
            var authorization = await authorizationFreshVerifier.VerifyAsync(
                plan.KnowledgeBaseIds,
                plan.DataSourceIds ?? [],
                task.UserId,
                cancellationToken);
            if (!authorization.IsSuccess)
            {
                return Result.From(authorization);
            }
        }

        if (task.TaskType == AgentTaskType.CloudDataReport && plan.CloudReadonlyIntent is null)
        {
            return InvalidPlan("CloudDataReport PlanDraft requires a typed Cloud readonly intent before confirmation.");
        }

        var catalogResult = await planToolGuard.GetAvailableToolCatalogAsync(
            task.UserId,
            simulationOnly,
            plan.BusinessDomains,
            cancellationToken,
            plan.SkillCode,
            plan.PluginSelectionMode);
        if (!catalogResult.IsSuccess)
        {
            return Result.From(catalogResult);
        }

        RuntimeAgentConfigurationSnapshot currentRoutingConfiguration;
        try
        {
            currentRoutingConfiguration = await routingSnapshotReader.ReadCurrentAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ReconfirmationRequired(
                "The authoritative routing prompt/model snapshot is unavailable; generate and confirm a new PlanDraft after configuration is restored.");
        }

        if (plan.ExecutionSnapshot is null ||
            !AgentPlanCatalogSnapshotAuthority.Matches(
                plan.ExecutionSnapshot,
                catalogResult.Value!,
                currentRoutingConfiguration,
                plan.DataSourceIds,
                plan.KnowledgeBaseIds,
                plan.IntentCandidates))
        {
            return ReconfirmationRequired(
                "Tool/provider/prompt/model configuration changed after PlanDraft sealing; generate and confirm a new PlanDraft.");
        }

        var authority = planContractAuthority ?? new AgentPlanDraftContractAuthority(
            new IntentResultToCandidateAdapter(),
            new AgentPlanCanonicalizer());
        var executableResult = authority.SealExecutable(plan);
        if (!executableResult.IsSuccess)
        {
            return Result.From(executableResult);
        }

        var executablePlan = executableResult.Value!;
        var executableIntegrity = integrityValidator.ValidatePersisted(
            executablePlan.CanonicalJson,
            requireExecutable: true);
        if (!executableIntegrity.IsSuccess)
        {
            return Result.From(executableIntegrity);
        }

        var approvalRequiredStepIndexes = guardedSteps
            .Select((step, index) => (step, index))
            .Where(item => item.step.RequiresApproval)
            .Select(item => item.index + 1)
            .ToArray();
        task.ConfirmExecutablePlan(
            executablePlan.CanonicalJson,
            approvalRequiredStepIndexes,
            now);
        return Result.Success();
    }

    private static bool StepsMatch(
        IReadOnlyCollection<AgentTaskPlanStepDocument> persisted,
        IReadOnlyCollection<AgentStepPlanDto> guarded)
    {
        var guardedDocuments = guarded
            .Select(step => new AgentTaskPlanStepDocument(
                step.Title,
                step.Description,
                step.StepType,
                step.ToolCode,
                step.RequiresApproval,
                step.InputJson))
            .ToArray();
        return string.Equals(
            CanonicalJson.Serialize(persisted),
            CanonicalJson.Serialize(guardedDocuments),
            StringComparison.Ordinal);
    }

    private static Result<AgentTaskPlanDocument> DeserializePlan(string planJson)
    {
        try
        {
            var plan = JsonSerializer.Deserialize<AgentTaskPlanDocument>(
                planJson,
                CanonicalJson.SerializerOptions);
            return plan is null
                ? InvalidPlan<AgentTaskPlanDocument>()
                : Result.Success(plan);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return InvalidPlan<AgentTaskPlanDocument>();
        }
    }

    private static Result<T> InvalidPlan<T>()
    {
        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentPlanInvalid,
            "Agent task plan JSON is invalid and cannot be confirmed."));
    }

    private static Result InvalidPlan(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(AppProblemCodes.AgentPlanInvalid, detail));
    }

    private static Result ReconfirmationRequired(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.ApprovalReconfirmationRequired,
            detail));
    }
}
