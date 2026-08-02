namespace AICopilot.Core.AiGateway.Aggregates.Sessions;

public sealed record MessageAddedToSessionEvent(
    Guid SessionId,
    string Content,
    MessageType Type,
    DateTime CreatedAtUtc);
