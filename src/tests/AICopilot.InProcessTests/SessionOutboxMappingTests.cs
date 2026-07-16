using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.EntityFrameworkCore.Outbox;

namespace AICopilot.InProcessTests;

public sealed class SessionOutboxMappingTests
{
    [Fact]
    public void OutboxMessage_FromSessionEvents_ShouldPreserveEventTypeAndPayload()
    {
        var sessionId = Guid.NewGuid();
        var messageEvent = new MessageAddedToSessionEvent(
            sessionId,
            "hello from outbox conversion test",
            MessageType.Assistant,
            DateTime.UtcNow);
        var attestationEvent = new OnsiteAttestationSetEvent(
            sessionId,
            "operator-a",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(5));

        var messageOutbox = OutboxMessage.FromIntegrationEvent(messageEvent);
        var attestationOutbox = OutboxMessage.FromIntegrationEvent(attestationEvent);

        messageOutbox.EventTypeName.Should().Be(typeof(MessageAddedToSessionEvent).FullName);
        messageOutbox.Payload.Should().Contain("hello from outbox conversion test");
        JsonDocument.Parse(messageOutbox.Payload)
            .RootElement
            .GetProperty(nameof(MessageAddedToSessionEvent.SessionId))
            .GetGuid()
            .Should()
            .Be(sessionId);

        attestationOutbox.EventTypeName.Should().Be(typeof(OnsiteAttestationSetEvent).FullName);
        attestationOutbox.Payload.Should().Contain("operator-a");
        JsonDocument.Parse(attestationOutbox.Payload)
            .RootElement
            .GetProperty(nameof(OnsiteAttestationSetEvent.SessionId))
            .GetGuid()
            .Should()
            .Be(sessionId);
    }
}
