namespace AICopilot.Core.AiGateway.Aggregates.Sessions;

public sealed record MessageModelSnapshot(
    Guid? FinalModelId,
    string? FinalModelName,
    int? ContextWindowTokens,
    int? MaxOutputTokens);
