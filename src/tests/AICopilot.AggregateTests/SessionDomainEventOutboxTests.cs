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
}
