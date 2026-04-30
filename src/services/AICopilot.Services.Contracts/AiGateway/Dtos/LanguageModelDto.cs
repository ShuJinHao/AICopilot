namespace AICopilot.Services.Contracts.AiGateway.Dtos;

public record LanguageModelDto
{
    public Guid Id { get; init; }
    public required string Provider { get; init; }
    public required string Name { get; init; }
    public required string BaseUrl { get; init; }
    public int MaxTokens { get; init; }
    public double Temperature { get; init; }
    public bool HasApiKey { get; init; }
    public string? ApiKeyMasked { get; init; }
}
