namespace AICopilot.Core.AiGateway.Aggregates.Sessions;

public sealed record MessageAddedToSessionEvent(
    Guid SessionId,
    string Content,
    MessageType Type,
    DateTime CreatedAtUtc);

public sealed record OnsiteAttestationSetEvent(
    Guid SessionId,
    string ConfirmedBy,
    DateTimeOffset ConfirmedAtUtc,
    DateTimeOffset ExpiresAtUtc);
