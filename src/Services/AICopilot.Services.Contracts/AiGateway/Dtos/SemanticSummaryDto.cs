namespace AICopilot.Services.Contracts.AiGateway.Dtos;

public sealed record SemanticMetricItemDto(
    string Name,
    string Label,
    string Value);

public sealed record SemanticSummaryDto(
    string Target,
    string Conclusion,
    IReadOnlyList<SemanticMetricItemDto> Metrics,
    IReadOnlyList<string> Highlights,
    string Scope);
