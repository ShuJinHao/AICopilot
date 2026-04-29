using AICopilot.Services.Contracts;
using MassTransit;

namespace AICopilot.EventBus;

public sealed class MassTransitIntegrationEventPublisher(IPublishEndpoint publishEndpoint) : IIntegrationEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        return publishEndpoint.Publish(message, cancellationToken);
    }
}
