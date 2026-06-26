using System.Text.Json;
using System.Text.Json.Serialization;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class AgentPlanDraftConfirmationService(
    AgentPlanToolGuard planToolGuard,
    ICloudReadonlyAgentPlanService cloudReadonlyPlanService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<Result> ConfirmAsync(
        AgentTask task,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var planResult = DeserializePlan(task.PlanJson);
        if (!planResult.IsSuccess)
        {
            return Result.From(planResult);
        }

        var plan = planResult.Value!;
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
        var cloudReadonlyIntentResult = await ResolveCloudReadonlyIntentAsync(task, plan, cancellationToken);
        if (!cloudReadonlyIntentResult.IsSuccess)
        {
            return Result.From(cloudReadonlyIntentResult);
        }

        var cloudReadonlyIntent = cloudReadonlyIntentResult.Value;
        var executablePlan = plan with
        {
            PlanKind = AgentTaskPlanKinds.ExecutablePlan,
            IsExecutable = true,
            CloudReadonlyIntent = cloudReadonlyIntent,
            Steps = guardedSteps
                .Select(step => new AgentTaskPlanStepDocument(
                    step.Title,
                    step.Description,
                    step.StepType,
                    step.ToolCode,
                    step.RequiresApproval,
                    step.InputJson))
                .ToArray(),
            ApprovalCheckpoints = guardedSteps
                .Where(step => step.RequiresApproval)
                .Select(step => string.IsNullOrWhiteSpace(step.ToolCode) ? step.Title : step.ToolCode!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ToolApprovalCheckpoints = guardedSteps
                .Where(step => step.RequiresApproval && !string.IsNullOrWhiteSpace(step.ToolCode))
                .Select(step => step.ToolCode!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        var approvalRequiredStepIndexes = guardedSteps
            .Select((step, index) => (step, index))
            .Where(item => item.step.RequiresApproval)
            .Select(item => item.index + 1)
            .ToArray();

        task.ConfirmExecutablePlan(
            JsonSerializer.Serialize(executablePlan, JsonOptions),
            approvalRequiredStepIndexes,
            now);
        return Result.Success();
    }

    private async Task<Result<AgentTaskPlanCloudReadonlyIntentDocument?>> ResolveCloudReadonlyIntentAsync(
        AgentTask task,
        AgentTaskPlanDocument plan,
        CancellationToken cancellationToken)
    {
        if (task.TaskType != AgentTaskType.CloudDataReport || plan.CloudReadonlyIntent is not null)
        {
            return Result.Success(plan.CloudReadonlyIntent);
        }

        var intentResult = await cloudReadonlyPlanService.CreateIntentAsync(
            task.SessionId.Value,
            task.Goal,
            cancellationToken);
        return intentResult.IsSuccess
            ? Result.Success<AgentTaskPlanCloudReadonlyIntentDocument?>(
                AgentTaskPlanCloudReadonlyIntentDocument.From(intentResult.Value!))
            : Result.From(intentResult);
    }

    private static Result<AgentTaskPlanDocument> DeserializePlan(string planJson)
    {
        try
        {
            var plan = JsonSerializer.Deserialize<AgentTaskPlanDocument>(planJson, JsonOptions);
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
}
