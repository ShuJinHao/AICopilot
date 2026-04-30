using AICopilot.Services.Contracts;

namespace AICopilot.EntityFrameworkCore.Outbox;

public sealed class OutboxIntegrationEventPublisher(OutboxDbContext dbContext) : IIntegrationEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        dbContext.OutboxMessages.Add(OutboxMessage.FromIntegrationEvent(message));
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
