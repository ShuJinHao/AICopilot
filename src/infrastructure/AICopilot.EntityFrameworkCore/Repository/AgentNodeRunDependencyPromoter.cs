using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

internal static class AgentNodeRunDependencyPromoter
{
    public static async Task PromoteAsync(
        AiGatewayDbContext context,
        AgentTaskRunAttemptId runAttemptId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var nodes = await context.AgentNodeRuns
            .Where(node => node.RunAttemptId == runAttemptId)
            .ToListAsync(cancellationToken);
        var changed = true;
        while (changed)
        {
            changed = false;
            var byNodeId = nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
            foreach (var candidate in nodes.Where(node => node.Status is
                         AgentNodeRunStatus.Pending or
                         AgentNodeRunStatus.WaitingApproval))
            {
                string[] dependencies;
                try
                {
                    dependencies = JsonSerializer.Deserialize<string[]>(candidate.DependenciesJson) ?? [];
                }
                catch (JsonException exception)
                {
                    throw new InvalidOperationException(
                        $"NodeRun '{candidate.NodeId}' contains invalid dependency JSON.",
                        exception);
                }

                var parents = dependencies.Select(dependency =>
                    byNodeId.TryGetValue(dependency, out var parent)
                        ? parent
                        : throw new InvalidOperationException(
                            $"NodeRun '{candidate.NodeId}' references unknown dependency '{dependency}'."))
                    .ToArray();
                var failedRequired = parents.FirstOrDefault(parent =>
                    parent.IsRequired &&
                    (parent.Status is AgentNodeRunStatus.Failed or AgentNodeRunStatus.Cancelled));
                var failedStrict = candidate.JoinPolicy != "OptionalBestEffort"
                    ? parents.FirstOrDefault(parent => parent.Status is
                        AgentNodeRunStatus.Failed or AgentNodeRunStatus.Cancelled)
                    : null;
                var failedParent = failedRequired ?? failedStrict;
                if (failedParent is not null)
                {
                    candidate.CancelFromDependencyFailure(failedParent.NodeId, nowUtc);
                    changed = true;
                    continue;
                }

                var dependenciesSatisfied = parents.All(parent =>
                    parent.Status == AgentNodeRunStatus.Succeeded ||
                    candidate.JoinPolicy == "OptionalBestEffort" &&
                    !parent.IsRequired &&
                    (parent.Status is AgentNodeRunStatus.Failed or AgentNodeRunStatus.Cancelled));
                if (dependenciesSatisfied && candidate.Status == AgentNodeRunStatus.Pending)
                {
                    candidate.MakeRunnable(nowUtc);
                    changed = true;
                }
            }
        }
    }
}
