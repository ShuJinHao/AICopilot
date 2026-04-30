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
        IEnumerable<BranchResult> branchResults)
    {
        logger.LogInformation("Context aggregation completed.");

        var generationContext = new GenerationContext
        {
            Request = request,
            Scene = scene
        };

        foreach (var result in branchResults)
        {
            switch (result.Type)
            {
                case BranchType.Tools when result.Tools != null:
                    generationContext.Tools = result.Tools;
                    break;

                case BranchType.Knowledge when !string.IsNullOrWhiteSpace(result.Knowledge):
                    generationContext.KnowledgeContext = result.Knowledge;
                    break;

                case BranchType.DataAnalysis when !string.IsNullOrWhiteSpace(result.DataAnalysis):
                    generationContext.DataAnalysisContext = result.DataAnalysis;
                    break;

                case BranchType.BusinessPolicy when !string.IsNullOrWhiteSpace(result.BusinessPolicy):
                    generationContext.BusinessPolicyContext = result.BusinessPolicy;
                    break;
            }
        }

        return generationContext;
    }
}
