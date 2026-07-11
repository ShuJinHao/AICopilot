using AICopilot.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Outbox;

public sealed class AiGatewayDomainEventOutboxSource : IPersistenceOutboxSource
{
    public bool Supports(DbContext dbContext)
    {
        return dbContext is AiGatewayDbContext;
    }

    public bool HasPending(DbContext dbContext)
    {
        return DomainEventEntities(dbContext).Count > 0;
    }

    public IReadOnlyCollection<OutboxMessage> Materialize(DbContext dbContext)
    {
        return DomainEventEntities(dbContext)
            .SelectMany(entity => entity.DomainEvents)
            .Select(OutboxMessage.FromIntegrationEvent)
            .ToArray();
    }

    public void CommitConfirmed(DbContext dbContext)
    {
        foreach (var entity in DomainEventEntities(dbContext))
        {
            entity.ClearDomainEvents();
        }
    }

    private static IReadOnlyCollection<IHasDomainEvents> DomainEventEntities(DbContext dbContext)
    {
        return dbContext.ChangeTracker
            .Entries()
            .Select(entry => entry.Entity)
            .OfType<IHasDomainEvents>()
            .Where(entity => entity.DomainEvents.Count > 0)
            .ToArray();
    }
}
