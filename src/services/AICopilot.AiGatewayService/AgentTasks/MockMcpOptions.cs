namespace AICopilot.AiGatewayService.AgentTasks;

public sealed class MockMcpOptions
{
    public const string SectionName = "AiGateway:MockMcp";

    public bool Enabled { get; init; }
}
