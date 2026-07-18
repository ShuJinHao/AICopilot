using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class AgentPlanDraftConfirmationService(
    AgentPlanToolGuard planToolGuard,
    ICloudReadonlyAgentPlanService? legacyCloudReadonlyPlanService = null,
    IAgentPlanIntegrityValidator? planIntegrityValidator = null)
{
    public async Task<Result> ConfirmAsync(
        AgentTask task,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        _ = legacyCloudReadonlyPlanService;
        var planResult = DeserializePlan(task.PlanJson);
        if (!planResult.IsSuccess)
        {
            return Result.From(planResult);
        }

        var integrityValidator = planIntegrityValidator ?? new AgentPlanCanonicalizer();
        var integrity = integrityValidator.ValidatePersisted(task.PlanJson);
        if (!integrity.IsSuccess)
        {
            return Result.From(integrity);
        }

        var plan = planResult.Value!;
        if (!string.Equals(plan.PlanKind, AgentTaskPlanKinds.PlanDraft, StringComparison.Ordinal) ||
            plan.IsExecutable)
        {
            return InvalidPlan("Only a sealed non-executable PlanDraft v2 can be confirmed.");
        }

        if (plan.Nodes is null || plan.Nodes.Count == 0 || plan.Steps.Count == 0)
        {
            return InvalidPlan("A capability-gap PlanDraft has no executable nodes and cannot be confirmed.");
        }

        if (task.TaskType == AgentTaskType.CloudDataReport && plan.CloudReadonlyIntent is null)
        {
            return InvalidPlan("CloudDataReport PlanDraft requires a typed Cloud readonly intent before confirmation.");
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
            plan.SkillCode);
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

        var catalogResult = await planToolGuard.GetAvailableToolCatalogAsync(
            task.UserId,
            plan.PlannerSafetySummary?.IsSimulationOnly ?? false,
            plan.BusinessDomains,
            cancellationToken,
            plan.SkillCode);
        if (!catalogResult.IsSuccess)
        {
            return Result.From(catalogResult);
        }

        if (!SnapshotMatches(plan.ExecutionSnapshot!, catalogResult.Value!))
        {
            return ReconfirmationRequired(
                "Tool/provider catalog changed after PlanDraft sealing; generate and confirm a new PlanDraft.");
        }

        var executablePlan = plan with
        {
            PlanKind = AgentTaskPlanKinds.ExecutablePlan,
            IsExecutable = true
        };
        var executableJson = CanonicalJson.Serialize(executablePlan);
        var executableIntegrity = integrityValidator.ValidatePersisted(
            executableJson,
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
            executableJson,
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

    private static bool SnapshotMatches(
        AgentExecutionSnapshotDocument snapshot,
        PlannerToolCatalog catalog)
    {
        return snapshot.ToolCatalogVersion == catalog.Version &&
               string.Equals(snapshot.ToolCatalogDigest, AgentPlanV2Compiler.HashToolCatalog(catalog.Tools), StringComparison.Ordinal) &&
               string.Equals(snapshot.ProviderCatalogDigest, AgentPlanV2Compiler.HashProviderCatalog(catalog.Tools), StringComparison.Ordinal) &&
               string.Equals(snapshot.PluginCatalogDigest, AgentPlanV2Compiler.HashToolSubset(catalog.Tools, "Plugin"), StringComparison.Ordinal) &&
               string.Equals(snapshot.McpCatalogDigest, AgentPlanV2Compiler.HashToolSubset(catalog.Tools, "Mcp"), StringComparison.Ordinal);
    }

    private static Result<AgentTaskPlanDocument> DeserializePlan(string planJson)
    {
        try
        {
            var plan = JsonSerializer.Deserialize<AgentTaskPlanDocument>(planJson, CanonicalJson.SerializerOptions);
            return plan is null
                ? InvalidPlan()
                : Result.Success(plan);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return InvalidPlan();
        }
    }

    private static Result<AgentTaskPlanDocument> InvalidPlan()
    {
        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentPlanInvalid,
            "Agent task plan JSON is invalid and cannot be confirmed."));
    }

    private static Result InvalidPlan(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.AgentPlanInvalid,
            detail));
    }

    private static Result ReconfirmationRequired(string detail)
    {
        return Result.Failure(new ApiProblemDescriptor(
            AppProblemCodes.ApprovalReconfirmationRequired,
            detail));
    }
}
