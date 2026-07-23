using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;

namespace AICopilot.AiGatewayService.AgentTasks;

internal sealed record AgentEvidenceJoinResult(
    string Status,
    string ResultType,
    string JoinPolicy,
    int RequiredEvidenceCount,
    int OptionalEvidenceCount,
    int MissingOptionalCount);

internal static class AgentRuntimeEvidenceJoinTool
{
    public static AgentEvidenceJoinResult Join(
        AgentTaskPlanDocument plan,
        AgentStep step,
        IReadOnlyCollection<AgentEvidenceRecord> inputEvidence)
    {
        var node = plan.Nodes?.ElementAtOrDefault(step.StepIndex - 1)
            ?? throw new InvalidOperationException("Evidence join is missing its frozen Node contract.");
        if (node.NodeKind != "JoinNode" ||
            node.JoinPolicy is not ("AllRequired" or "OptionalBestEffort") ||
            node.DependsOn.Count < 2 ||
            !node.EvidenceSelectors.SequenceEqual(node.DependsOn, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Evidence join Node contract is invalid.");
        }

        var byId = (plan.Nodes ?? []).ToDictionary(candidate => candidate.NodeId, StringComparer.Ordinal);
        var evidenceNodeIds = inputEvidence
            .Select(evidence => evidence.NodeId)
            .ToHashSet(StringComparer.Ordinal);
        var dependencies = node.DependsOn.Select(dependencyId =>
            byId.TryGetValue(dependencyId, out var dependency)
                ? dependency
                : throw new InvalidOperationException("Evidence join references an unknown dependency."))
            .ToArray();
        var missingRequired = dependencies
            .Where(dependency => dependency.Required && !evidenceNodeIds.Contains(dependency.NodeId))
            .Select(dependency => dependency.NodeId)
            .ToArray();
        if (missingRequired.Length != 0)
        {
            throw new InvalidOperationException("Evidence join is missing required parent Evidence.");
        }

        var requiredCount = dependencies.Count(dependency => dependency.Required);
        var optionalCount = dependencies.Length - requiredCount;
        var missingOptionalCount = dependencies.Count(dependency =>
            !dependency.Required && !evidenceNodeIds.Contains(dependency.NodeId));
        return new AgentEvidenceJoinResult(
            "completed",
            "evidence-join",
            node.JoinPolicy,
            requiredCount,
            optionalCount,
            missingOptionalCount);
    }
}
