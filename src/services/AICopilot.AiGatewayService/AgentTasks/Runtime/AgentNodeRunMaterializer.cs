using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed class AgentNodeRunMaterializer(
    IAgentNodeRunStore nodeRunStore)
{
    public Task<IReadOnlyCollection<AgentNodeRun>> EnsureMaterializedAsync(
        DurableTaskClaim claim,
        AgentTaskPlanDocument plan,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(plan.SchemaVersion, AgentPlanContractVersions.PlanV2, StringComparison.Ordinal) ||
            plan.TopologyProfile is not ("LinearV1" or "DagV1") ||
            string.IsNullOrWhiteSpace(plan.PlanDigest) ||
            plan.ExecutionSnapshot is null ||
            plan.ConcurrencyPolicy is null ||
            (plan.TopologyProfile == "LinearV1" && plan.ConcurrencyPolicy.MaxParallelism != 1) ||
            (plan.TopologyProfile == "DagV1" &&
             (plan.ConcurrencyPolicy.MaxParallelism is < AgentPlanContractVersions.DagMinParallelism
                 or > AgentPlanContractVersions.DagMaxParallelism)) ||
            plan.Nodes is not { Count: > 0 } ||
            plan.Nodes.Count != claim.Task.Steps.Count)
        {
            throw new InvalidOperationException(
                "Durable NodeRun materialization requires a complete executable LinearV1 or bounded DagV1 Plan v2.");
        }

        var executionSnapshotJson = CanonicalJson.Serialize(plan.ExecutionSnapshot);
        var executionSnapshotDigest = CanonicalJson.ComputeSha256(executionSnapshotJson);
        var planBudget = plan.Budgets ?? throw new InvalidOperationException(
            "Executable Plan v2 is missing its immutable task budget.");
        var taskBudget = new AgentRunBudgetLimits(
            planBudget.PolicyVersion,
            planBudget.MaxNodes,
            planBudget.MaxToolCalls,
            planBudget.MaxModelCalls,
            planBudget.MaxInputTokens,
            planBudget.MaxOutputTokens,
            planBudget.MaxElapsedSeconds,
            planBudget.MaxCostAmount,
            planBudget.CostCurrency,
            planBudget.MaxRetries,
            planBudget.MaxArtifactCount,
            planBudget.MaxArtifactBytes);
        var orderedSteps = claim.Task.Steps.OrderBy(step => step.StepIndex).ToArray();
        var seeds = plan.Nodes.Select((node, index) =>
        {
            var step = orderedSteps[index];
            var toolCode = node.RequestedToolCodes.SingleOrDefault();
            if (!string.Equals(toolCode, step.ToolCode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Plan node '{node.NodeId}' is not bound one-to-one to runtime step {step.StepIndex}.");
            }

            var inputJson = string.IsNullOrWhiteSpace(node.Input?.CanonicalInputJson)
                ? step.InputJson ?? "{}"
                : node.Input.CanonicalInputJson!;
            var canonicalInput = CanonicalJson.Canonicalize(inputJson);
            var dependencies = node.DependsOn
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var dependenciesJson = CanonicalJson.Serialize(dependencies);
            var sideEffectClass = node.SideEffectClass switch
            {
                "ReadOnly" => AgentNodeSideEffectClass.ReadOnly,
                "DeterministicInternal" => AgentNodeSideEffectClass.DeterministicInternal,
                "ArtifactDraftOnly" or "ArtifactWrite" => AgentNodeSideEffectClass.ArtifactWrite,
                "ExternalIdempotent" => AgentNodeSideEffectClass.ExternalIdempotent,
                "ExternalOutcomeUnknown" => AgentNodeSideEffectClass.ExternalOutcomeUnknown,
                _ => throw new InvalidOperationException(
                    $"Plan node '{node.NodeId}' has an unsupported side-effect class.")
            };
            var idempotencyKeyHash = CanonicalJson.ComputeSha256(CanonicalJson.Serialize(new
            {
                taskId = claim.Task.Id.Value,
                runAttemptId = claim.RunAttempt.Id.Value,
                planDigest = plan.PlanDigest,
                nodeId = node.NodeId,
                inputDigest = CanonicalJson.ComputeSha256(canonicalInput),
                policy = node.IdempotencyPolicy.PolicyVersion,
                mode = node.IdempotencyPolicy.Mode
            }));
            return new AgentNodeRunSeed(
                plan.PlanDigest!,
                executionSnapshotDigest,
                node.NodeId,
                node.NodeKind,
                toolCode,
                dependenciesJson,
                canonicalInput,
                CanonicalJson.ComputeSha256(canonicalInput),
                node.OutputSchemaRef,
                node.Required,
                node.ApprovalPolicy.Required,
                sideEffectClass,
                idempotencyKeyHash,
                node.RetryPolicy.MaxAttempts,
                node.TimeoutPolicy.TimeoutSeconds,
                new AgentNodeBudgetLimits(
                    node.Budget.MaxToolCalls,
                    node.Budget.MaxModelCalls,
                    node.Budget.MaxInputTokens,
                    node.Budget.MaxOutputTokens,
                    node.Budget.MaxCostAmount,
                    node.Budget.MaxArtifactCount,
                    node.Budget.MaxArtifactBytes),
                node.JoinPolicy,
                IsInitiallyRunnable: dependencies.Length == 0 && !node.ApprovalPolicy.Required);
        }).ToArray();

        return nodeRunStore.EnsureMaterializedAsync(
            claim,
            taskBudget,
            seeds,
            nowUtc,
            cancellationToken);
    }

    public Task<AgentFencedWriteResult> ReleaseApprovedNodeAsync(
        AgentNodeRunId nodeRunId,
        DurableTaskClaim claim,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        return nodeRunStore.TryReleaseApprovalAsync(
            nodeRunId,
            claim.RunAttempt.Id,
            claim.TaskFencingToken,
            nowUtc,
            cancellationToken);
    }
}
