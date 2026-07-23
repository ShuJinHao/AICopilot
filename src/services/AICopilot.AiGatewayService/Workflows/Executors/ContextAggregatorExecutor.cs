using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Safety;
using Microsoft.Extensions.Logging;

namespace AICopilot.AiGatewayService.Workflows.Executors;

public class ContextAggregatorExecutor(ILogger<ContextAggregatorExecutor> logger)
{
    public const string ExecutorId = nameof(ContextAggregatorExecutor);

    public GenerationContext Execute(
        ChatStreamRequest request,
        ManufacturingSceneType scene,
        IEnumerable<BranchResult> branchResults,
        AgentTaskChatEvidenceContext? boundTaskEvidence = null)
    {
        logger.LogInformation("Context aggregation completed.");

        var materializedBranchResults = branchResults.ToArray();

        var generationContext = new GenerationContext
        {
            Request = request,
            Scene = scene,
            BoundTaskEvidence = boundTaskEvidence,
            Evidence = materializedBranchResults
                .Where(result => result.Status == BranchExecutionStatus.Succeeded)
                .SelectMany(result => result.Evidence)
                .OrderBy(evidence => evidence.NodeId, StringComparer.Ordinal)
                .ToArray(),
            RequiredEmptyBranches = materializedBranchResults
                .Where(result => result.IsRequired && result.Status == BranchExecutionStatus.Empty)
                .Select(result => result.Type)
                .Distinct()
                .OrderBy(value => value)
                .ToArray()
        };

        if (boundTaskEvidence is not null && generationContext.Evidence.Count != 0)
        {
            throw new InvalidOperationException(
                "Bound AgentTask Evidence cannot be mixed with newly produced Chat branch Evidence.");
        }

        generationContext.EvidenceSetDigest = boundTaskEvidence?.EvidenceSetDigest ??
            (generationContext.Evidence.Count == 0
                ? string.Empty
                : AgentWorkflowEvidenceNormalizer.ComputeEvidenceSetDigest(generationContext.Evidence));

        foreach (var result in materializedBranchResults)
        {
            if (result.Status != BranchExecutionStatus.Succeeded)
            {
                continue;
            }

            switch (result.Type)
            {
                case BranchType.Tools when result.Tools != null:
                    generationContext.Tools = result.Tools;
                    break;

                case BranchType.Knowledge:
                    generationContext.KnowledgeContext = JoinEvidenceContext(result.Evidence);
                    break;

                case BranchType.DataAnalysis:
                    generationContext.DataAnalysisContext = JoinEvidenceContext(result.Evidence);
                    break;

                case BranchType.BusinessPolicy:
                    generationContext.BusinessPolicyContext = JoinEvidenceContext(result.Evidence);
                    break;
            }
        }

        return generationContext;
    }

    private static string JoinEvidenceContext(IEnumerable<AgentWorkflowEvidence> evidence)
    {
        return string.Join(
            Environment.NewLine,
            evidence
                .Where(item => !string.IsNullOrWhiteSpace(item.SafeContext))
                .OrderBy(item => item.NodeId, StringComparer.Ordinal)
                .Select(item => item.SafeContext));
    }
}
