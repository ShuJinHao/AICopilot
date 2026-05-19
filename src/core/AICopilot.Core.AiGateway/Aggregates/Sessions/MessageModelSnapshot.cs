namespace AICopilot.Core.AiGateway.Aggregates.Sessions;

public sealed record MessageModelSnapshot(
    Guid? FinalModelId,
    string? FinalModelName,
    Guid? RoutingModelId,
    string? RoutingModelName,
    int? ContextWindowTokens,
    int? MaxOutputTokens);
