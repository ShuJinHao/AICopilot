namespace AICopilot.Services.Contracts.AiGateway.Dtos;

public record ConversationTemplateDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Code { get; init; }
    public required string Description { get; init; }
    public required string SystemPrompt { get; init; }
    public Guid ModelId { get; init; }
    public string Scope { get; init; } = "General";
    public int BuiltInVersion { get; init; }
    public bool IsBuiltIn { get; init; }
    public int? MaxTokens { get; init; }
    public double? Temperature { get; init; }
    public bool IsEnabled { get; init; }
}
