namespace AICopilot.Services.Contracts.AiGateway.Dtos;

public record LanguageModelDto
{
    public Guid Id { get; init; }
    public required string Provider { get; init; }
    public required string ProtocolType { get; init; }
    public required string Name { get; init; }
    public required string BaseUrl { get; init; }
    public int MaxTokens { get; init; }
    public int ContextWindowTokens { get; init; }
    public int MaxOutputTokens { get; init; }
    public double Temperature { get; init; }
    public bool IsEnabled { get; init; }
    public IReadOnlyList<string> Usages { get; init; } = [];
    public bool HasApiKey { get; init; }
    public string? ApiKeyPreview { get; init; }
    public required string ConnectivityStatus { get; init; }
    public DateTimeOffset? ConnectivityCheckedAt { get; init; }
    public string? ConnectivityError { get; init; }
}

public record SelectableChatModelDto
{
    public Guid Id { get; init; }
    public required string Provider { get; init; }
    public required string ProtocolType { get; init; }
    public required string Name { get; init; }
    public int ContextWindowTokens { get; init; }
    public int MaxOutputTokens { get; init; }
}

public record RoutingModelConfigurationDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid ModelId { get; init; }
    public required string ModelName { get; init; }
    public required string ModelProvider { get; init; }
    public bool IsActive { get; init; }
}

public record LanguageModelTestResultDto
{
    public bool Success { get; init; }
    public required string Status { get; init; }
    public required string Message { get; init; }
    public string? Error { get; init; }
    public long ElapsedMilliseconds { get; init; }
    public DateTimeOffset CheckedAt { get; init; }
}
