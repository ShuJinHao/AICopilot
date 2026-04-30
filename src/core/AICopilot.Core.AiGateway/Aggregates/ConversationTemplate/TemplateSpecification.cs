namespace AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;

public record TemplateSpecification
{
    public int? MaxTokens { get; init; }
    public float? Temperature { get; init; }
}
