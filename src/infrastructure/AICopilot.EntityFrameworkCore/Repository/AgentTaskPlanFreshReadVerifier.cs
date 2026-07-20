using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

/// <summary>
/// Creates a new DbContext for every verification so execution gates never
/// accept values from the caller's tracking context or identity map.
/// </summary>
public sealed class AgentTaskPlanFreshReadVerifier(
    DbContextOptions<AiGatewayDbContext> options)
    : IAgentTaskPlanFreshReadVerifier
{
    public async Task<AgentTaskPlanFreshReadDecision> VerifyAsync(
        AgentTaskPlanFreshReadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TaskId == Guid.Empty)
        {
            throw new ArgumentException("Agent task id cannot be empty.", nameof(request));
        }

        ArgumentNullException.ThrowIfNull(request.ExpectedPlanJson);

        await using var freshContext = new AiGatewayDbContext(options);
        var taskId = new AgentTaskId(request.TaskId);
        var persisted = await freshContext.AgentTasks
            .AsNoTracking()
            .Include(task => task.Steps)
            .Where(task => task.Id == taskId)
            .SingleOrDefaultAsync(cancellationToken);

        if (persisted is null)
        {
            return Mismatch("Persisted agent task could not be independently reloaded.");
        }

        if (persisted.Status != request.ExpectedStatus)
        {
            return Mismatch("Persisted agent task status changed; reload the task before continuing.");
        }

        if (!string.Equals(persisted.PlanJson, request.ExpectedPlanJson, StringComparison.Ordinal))
        {
            return Mismatch("Persisted agent task plan changed; reload and re-confirm before continuing.");
        }

        var persistedSteps = persisted.Steps
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
        if (!persistedSteps.SequenceEqual(request.ExpectedExecutionSteps))
        {
            return Mismatch(
                "Persisted agent steps changed outside the confirmed Plan; reload and re-confirm before continuing.");
        }

        return AgentTaskPlanFreshReadDecision.Match;
    }

    private static AgentTaskPlanFreshReadDecision Mismatch(string safeDetail)
    {
        return AgentTaskPlanFreshReadDecision.Mismatch(
            AppProblemCodes.AgentPlanInvalid,
            safeDetail);
    }
}
