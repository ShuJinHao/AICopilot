namespace AICopilot.Services.Contracts.AiGateway.Dtos;

public record ConversationTemplateDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string SystemPrompt { get; init; }
    public Guid ModelId { get; init; }
    public int? MaxTokens { get; init; }
    public double? Temperature { get; init; }
    public bool IsEnabled { get; init; }
}
