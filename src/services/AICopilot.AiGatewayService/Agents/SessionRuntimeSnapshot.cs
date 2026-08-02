namespace AICopilot.AiGatewayService.Agents;

public sealed class SessionRuntimeSnapshot
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid TemplateId { get; init; }
    public required string Title { get; init; }
}
