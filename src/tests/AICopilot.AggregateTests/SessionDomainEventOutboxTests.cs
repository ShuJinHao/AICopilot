using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;

namespace AICopilot.AggregateTests;

public sealed class SessionDomainEventOutboxTests
{
    [Fact]
    public void AddMessage_ShouldAppendMessageAddedDomainEvent()
    {
        var session = new Session(Guid.NewGuid(), ConversationTemplateId.New());

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
        var session = new Session(Guid.NewGuid(), ConversationTemplateId.New());
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
}
