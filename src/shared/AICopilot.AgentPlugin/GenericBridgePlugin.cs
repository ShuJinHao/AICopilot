using AICopilot.SharedKernel.Ai;

namespace AICopilot.AgentPlugin;

/// <summary>
/// MCP 通用桥接插件。
/// 该类实现了 IAgentPlugin 接口，用于将外部 MCP 服务适配为内部的原生插件。
/// 它不包含具体的业务逻辑，而是作为 MCP 工具集的容器。
/// </summary>
public class GenericBridgePlugin : IAgentPlugin
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public IEnumerable<AiToolDefinition>? Tools { get; init; }

    public IEnumerable<AiToolDefinition>? GetTools()
    {
        return Tools;
    }

    public IEnumerable<string>? HighRiskTools { get; init; }

    public ChatExposureMode ChatExposureMode { get; init; } = ChatExposureMode.Disabled;
}
