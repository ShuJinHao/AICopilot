namespace AICopilot.AiGatewayService.Agents;

public sealed class SessionRuntimeSnapshot
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public required string Title { get; init; }
    public DateTimeOffset? OnsiteConfirmedAt { get; init; }
    public string? OnsiteConfirmedBy { get; init; }
    public DateTimeOffset? OnsiteConfirmationExpiresAt { get; init; }

    public bool HasValidOnsiteAttestation(DateTimeOffset nowUtc)
    {
        return OnsiteConfirmedAt.HasValue
               && !string.IsNullOrWhiteSpace(OnsiteConfirmedBy)
               && OnsiteConfirmationExpiresAt.HasValue
               && OnsiteConfirmationExpiresAt.Value > nowUtc;
    }
}
