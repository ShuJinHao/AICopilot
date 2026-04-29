namespace AICopilot.Services.Contracts;

public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
        where TEvent : class;
}
