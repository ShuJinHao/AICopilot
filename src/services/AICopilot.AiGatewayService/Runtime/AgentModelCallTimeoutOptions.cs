namespace AICopilot.AiGatewayService.Runtime;

public sealed class AgentModelCallTimeoutOptions
{
    public const string SectionName = "AiGateway:ModelCallTimeouts";

    public int RoutingSeconds { get; init; } = 15;

    public int PlannerSeconds { get; init; } = 30;

    public TimeSpan RoutingTimeout => TimeSpan.FromSeconds(Math.Clamp(RoutingSeconds, 1, 120));

    public TimeSpan PlannerTimeout => TimeSpan.FromSeconds(Math.Clamp(PlannerSeconds, 1, 180));
}
