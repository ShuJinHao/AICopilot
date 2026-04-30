using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.EntityFrameworkCore.Outbox;
using System.Text.Json;

namespace AICopilot.BackendTests;

public sealed class SessionDomainEventOutboxTests
{
    [Fact]
    public void AddMessage_ShouldAppendMessageAddedDomainEvent()
    {
        var session = new Session(Guid.NewGuid(), Guid.NewGuid());

        session.AddMessage("hello from domain event test", MessageType.User);

        var domainEvent = session.DomainEvents
            .OfType<MessageAddedToSessionEvent>()
            .Single();

        domainEvent.SessionId.Should().Be(session.Id);
        domainEvent.Content.Should().Be("hello from domain event test");
        domainEvent.Type.Should().Be(MessageType.User);
        domainEvent.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void SetOnsiteAttestation_ShouldAppendOnsiteAttestationDomainEvent()
    {
        var session = new Session(Guid.NewGuid(), Guid.NewGuid());
        var confirmedAt = DateTimeOffset.UtcNow;
        var expiresAt = confirmedAt.AddMinutes(5);

        session.SetOnsiteAttestation(" operator-a ", confirmedAt, expiresAt);

        var domainEvent = session.DomainEvents
            .OfType<OnsiteAttestationSetEvent>()
            .Single();

        domainEvent.SessionId.Should().Be(session.Id);
        domainEvent.ConfirmedBy.Should().Be("operator-a");
        domainEvent.ConfirmedAtUtc.Should().Be(confirmedAt);
        domainEvent.ExpiresAtUtc.Should().Be(expiresAt);
    }

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
