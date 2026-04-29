namespace AICopilot.Core.AiGateway.Aggregates.LanguageModel;

public record ModelParameters
{
    public int MaxTokens { get; init; }
    public float Temperature { get; init; } = 0.7f;
}
