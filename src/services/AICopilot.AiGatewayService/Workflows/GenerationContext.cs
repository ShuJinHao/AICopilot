using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Safety;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.Workflows;

public class GenerationContext
{
    public required ChatStreamRequest Request { get; init; }

    public AiToolDefinition[] Tools { get; set; } = [];

    public string KnowledgeContext { get; set; } = string.Empty;

    public string DataAnalysisContext { get; set; } = string.Empty;

    public string BusinessPolicyContext { get; set; } = string.Empty;

    public ManufacturingSceneType Scene { get; set; } = ManufacturingSceneType.FallbackToExistingRouting;
}
