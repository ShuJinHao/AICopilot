using AICopilot.SharedKernel.Ai;

namespace AICopilot.AgentPlugin;

public interface IAgentPlugin
{
    string Name { get; }

    string Description { get; }

    IEnumerable<AiToolDefinition>? GetTools();

    IEnumerable<string>? HighRiskTools { get; }

    ChatExposureMode ChatExposureMode { get; }
}
