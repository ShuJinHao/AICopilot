using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AICopilot.AiGatewayService.AgentTasks;

/// <summary>
/// Single application owner for the pre-confirmation and pre-execution plan gate.
/// The gate first reads the caller's local canonical execution contract and
/// requires the tracked steps to match it exactly. The independent persistence
/// verifier then proves that those bytes, status, and steps still match the
/// current database row before persisted canonical/digest/executable validation.
/// </summary>
public sealed class AgentTaskPlanFreshReadGate(
    IAgentTaskPlanFreshReadVerifier freshReadVerifier,
    IAgentPlanIntegrityValidator planIntegrityValidator,
    IOptions<CloudReadonlyOptions>? cloudReadonlyOptions = null,
    IHostEnvironment? hostEnvironment = null)
{
    public async Task<Result<AgentPlanContractMetadata>> VerifyAsync(
        AgentTask task,
        bool requireExecutable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);

        var simulationProfile = VerifyDevelopmentSimulationProfile(task.PlanJson);
        if (!simulationProfile.IsSuccess)
        {
            return Result.From(simulationProfile);
        }

        var executionContract = planIntegrityValidator.ReadExecutionContract(
            task.PlanJson,
            requireExecutable);
        if (!executionContract.IsSuccess)
        {
            return Result.From(executionContract);
        }

        var trackedSteps = task.Steps
            .OrderBy(step => step.StepIndex)
            .Select(step => new AgentTaskPlanExecutionStepContract(
                step.StepIndex,
                step.Title,
                step.Description,
                step.StepType,
                step.ToolCode,
                step.RequiresApproval,
                step.InputJson))
            .ToArray();
        if (!trackedSteps.SequenceEqual(executionContract.Value!))
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                "Tracked agent steps do not exactly match the confirmed canonical Plan execution contract."));
        }

        var freshRead = await freshReadVerifier.VerifyAsync(
            new AgentTaskPlanFreshReadRequest(
                task.Id.Value,
                task.PlanJson,
                task.Status,
                executionContract.Value!),
            cancellationToken);
        if (!freshRead.IsMatch)
        {
            return Result.Failure(new ApiProblemDescriptor(
                freshRead.ErrorCode ?? AppProblemCodes.AgentPlanInvalid,
                freshRead.SafeDetail ?? "Persisted agent task plan failed independent verification."));
        }

        return planIntegrityValidator.ValidatePersisted(task.PlanJson, requireExecutable);
    }

    private Result VerifyDevelopmentSimulationProfile(string planJson)
    {
        AgentTaskPlanDocument? plan;
        try
        {
            plan = JsonSerializer.Deserialize<AgentTaskPlanDocument>(
                planJson,
                CanonicalJson.SerializerOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                "Agent task plan JSON is invalid."));
        }

        if (plan?.PlannerSafetySummary?.IsSimulationOnly != true)
        {
            return Result.Success();
        }

        var options = cloudReadonlyOptions?.Value;
        if (hostEnvironment?.IsDevelopment() != true ||
            options is null ||
            options.Mode != CloudReadonlyDataSourceMode.Simulation ||
            !options.Simulation.Enabled ||
            !options.Simulation.AlwaysMarkAsSimulation)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.AgentPlanInvalid,
                "Simulation Agent execution is disabled. Regenerate the plan under the explicit Development Simulation profile; Real/Cloud execution will not fall back to Simulation."));
        }

        return Result.Success();
    }
}
