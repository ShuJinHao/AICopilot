using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Outbox;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Transactions;

public sealed class RepositoryPersistenceCommitter(
    AuditDbContext auditDbContext,
    PersistenceCommitEngine commitEngine,
    IEnumerable<IPersistenceOutboxSource> outboxSources)
{
    public Task<int> SaveChangesAsync(
        DbContext businessDbContext,
        CancellationToken cancellationToken = default)
    {
        var stagedAuditEntries = auditDbContext.ChangeTracker
            .Entries<AuditLogEntry>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToArray();
        var outboxSource = outboxSources.SingleOrDefault(source => source.Supports(businessDbContext));
        if (!businessDbContext.ChangeTracker.HasChanges()
            && stagedAuditEntries.Length == 0
            && outboxSource?.HasPending(businessDbContext) != true)
        {
            return Task.FromResult(0);
        }

        var participant = new RepositoryCommitParticipant(
            businessDbContext,
            auditDbContext,
            stagedAuditEntries,
            outboxSource);

        return commitEngine.CommitAsync(
            $"Repository:{businessDbContext.GetType().Name}",
            participant,
            cancellationToken);
    }

    private sealed class RepositoryCommitParticipant(
        DbContext businessDbContext,
        AuditDbContext stagedAuditDbContext,
        IReadOnlyCollection<AuditLogEntry> stagedAuditEntries,
        IPersistenceOutboxSource? outboxSource) : IPersistenceCommitParticipant<int>
    {
        public DbContext TransactionOwner => businessDbContext;

        public async Task<int> PersistAttemptAsync(
            PersistenceAttemptContext context,
            CancellationToken cancellationToken)
        {
            var affectedRows = await businessDbContext.SaveChangesAsync(
                acceptAllChangesOnSuccess: false,
                cancellationToken);

            if (outboxSource is not null)
            {
                var outboxMessages = outboxSource.Materialize(businessDbContext);
                if (outboxMessages.Count > 0)
                {
                    await using var outboxDbContext = await context.CreateOutboxDbContextAsync(cancellationToken);
                    outboxDbContext.OutboxMessages.AddRange(outboxMessages);
                    affectedRows += await outboxDbContext.SaveChangesAsync(cancellationToken);
                }
            }

            if (stagedAuditEntries.Count > 0)
            {
                await using var auditDbContext = await context.CreateAuditDbContextAsync(cancellationToken);
                auditDbContext.AuditLogs.AddRange(stagedAuditEntries);
                affectedRows += await auditDbContext.SaveChangesAsync(cancellationToken);
            }

            return affectedRows;
        }

        public void CommitConfirmed(int result)
        {
            businessDbContext.ChangeTracker.AcceptAllChanges();
            outboxSource?.CommitConfirmed(businessDbContext);

            foreach (var entry in stagedAuditDbContext.ChangeTracker.Entries<AuditLogEntry>())
            {
                if (stagedAuditEntries.Contains(entry.Entity))
                {
                    entry.State = EntityState.Detached;
                }
            }
        }
    }
}
