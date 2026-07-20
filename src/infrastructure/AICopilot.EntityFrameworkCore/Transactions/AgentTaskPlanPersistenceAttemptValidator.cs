using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Transactions;

public interface IRepositoryPersistenceAttemptValidator
{
    bool Supports(DbContext businessDbContext);

    Task ValidateAsync(
        DbContext businessDbContext,
        PersistenceAttemptContext attemptContext,
        CancellationToken cancellationToken);
}

public sealed class AgentTaskPlanPersistenceAttemptValidator(
    IEnumerable<IAgentTaskPlanPersistencePolicy> policies)
    : IRepositoryPersistenceAttemptValidator
{
    public bool Supports(DbContext businessDbContext)
    {
        return businessDbContext is AiGatewayDbContext;
    }

    public async Task ValidateAsync(
        DbContext businessDbContext,
        PersistenceAttemptContext attemptContext,
        CancellationToken cancellationToken)
    {
        var expectedTaskWrites = businessDbContext.ChangeTracker
            .Entries<AgentTask>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Select(entry => entry.Entity.Id.Value)
            .ToArray();
        var expectedStepWrites = businessDbContext.ChangeTracker
            .Entries<AgentStep>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Entity.TaskId.Value)
            .ToArray();
        var expectedTaskIds = expectedTaskWrites
            .Concat(expectedStepWrites)
            .Distinct()
            .ToArray();
        if (expectedTaskIds.Length == 0)
        {
            return;
        }

        var configuredPolicies = policies.ToArray();
        if (configuredPolicies.Length != 1)
        {
            throw new InvalidOperationException(
                $"AgentTask persistence requires exactly one {nameof(IAgentTaskPlanPersistencePolicy)}; configured={configuredPolicies.Length}.");
        }

        var policy = configuredPolicies[0];
        await using var freshContext = await attemptContext.CreateAiGatewayDbContextAsync(cancellationToken);
        foreach (var expectedTaskId in expectedTaskIds)
        {
            var persisted = await freshContext.AgentTasks
                .AsNoTracking()
                .Include(task => task.Steps)
                .SingleOrDefaultAsync(task => task.Id == new AgentTaskId(expectedTaskId), cancellationToken);
            if (persisted is null)
            {
                throw IntegrityFailure(
                    expectedTaskId,
                    "agent_plan_persistence_roundtrip_failed",
                    "Fresh-context verification could not reload the AgentTask inside the persistence transaction.");
            }

            var trackedTask = businessDbContext.ChangeTracker
                .Entries<AgentTask>()
                .Select(entry => entry.Entity)
                .SingleOrDefault(task => task.Id.Value == expectedTaskId);
            if (trackedTask is not null &&
                !string.Equals(persisted.PlanJson, trackedTask.PlanJson, StringComparison.Ordinal))
            {
                throw IntegrityFailure(
                    expectedTaskId,
                    "agent_plan_persistence_roundtrip_failed",
                    "Fresh-context verification did not preserve the exact canonical Plan JSON bytes.");
            }

            var decision = policy.Validate(new AgentTaskPlanPersistenceValidationRequest(
                expectedTaskId,
                persisted.PlanJson));
            if (!decision.IsValid)
            {
                throw IntegrityFailure(
                    expectedTaskId,
                    decision.ErrorCode ?? "agent_plan_invalid",
                    decision.SafeDetail ?? "Persisted AgentTask Plan JSON failed integrity validation.");
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
            if (decision.ExecutionSteps is null ||
                !persistedSteps.SequenceEqual(decision.ExecutionSteps))
            {
                throw IntegrityFailure(
                    expectedTaskId,
                    AppProblemCodes.AgentPlanInvalid,
                    "Persisted AgentStep execution fields do not exactly match the canonical Plan execution contract.");
            }
        }
    }

    private static AgentTaskPlanPersistenceIntegrityException IntegrityFailure(
        Guid taskId,
        string code,
        string detail)
    {
        return new AgentTaskPlanPersistenceIntegrityException(taskId, code, detail);
    }

}
