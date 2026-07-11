using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Outbox;

public sealed class RagIntegrationEventBuffer : IIntegrationEventStager, IPersistenceOutboxSource
{
    private readonly List<Func<object>> factories = [];

    public void Stage<TEvent>(Func<TEvent> messageFactory)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(messageFactory);
        factories.Add(() => messageFactory());
    }

    public bool Supports(DbContext dbContext)
    {
        return dbContext is RagDbContext;
    }

    public bool HasPending(DbContext dbContext)
    {
        return factories.Count > 0;
    }

    public IReadOnlyCollection<OutboxMessage> Materialize(DbContext dbContext)
    {
        return factories
            .Select(factory => OutboxMessage.FromIntegrationEvent(factory()))
            .ToArray();
    }

    public void CommitConfirmed(DbContext dbContext)
    {
        factories.Clear();
    }
}
