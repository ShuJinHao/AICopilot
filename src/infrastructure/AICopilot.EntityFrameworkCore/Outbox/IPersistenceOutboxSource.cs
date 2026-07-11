using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Outbox;

public interface IPersistenceOutboxSource
{
    bool Supports(DbContext dbContext);

    bool HasPending(DbContext dbContext);

    IReadOnlyCollection<OutboxMessage> Materialize(DbContext dbContext);

    void CommitConfirmed(DbContext dbContext);
}
