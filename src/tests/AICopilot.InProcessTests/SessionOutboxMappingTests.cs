using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.EntityFrameworkCore.Outbox;

namespace AICopilot.InProcessTests;

public sealed class SessionOutboxMappingTests
{
    [Fact]
    public void OutboxMessage_FromMessageEvent_ShouldPreserveEventTypeAndPayload()
    {
        var sessionId = Guid.NewGuid();
        var messageEvent = new MessageAddedToSessionEvent(
            sessionId,
            "hello from outbox conversion test",
            MessageType.Assistant,
            DateTime.UtcNow);
        var messageOutbox = OutboxMessage.FromIntegrationEvent(messageEvent);

        messageOutbox.EventTypeName.Should().Be(typeof(MessageAddedToSessionEvent).FullName);
        messageOutbox.Payload.Should().Contain("hello from outbox conversion test");
        JsonDocument.Parse(messageOutbox.Payload)
            .RootElement
            .GetProperty(nameof(MessageAddedToSessionEvent.SessionId))
            .GetGuid()
            .Should()
            .Be(sessionId);

    }
}
