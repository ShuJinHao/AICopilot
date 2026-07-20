using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Transactions;

public sealed class RepositoryPersistenceCommitter(
    AuditDbContext auditDbContext,
    PersistenceCommitEngine commitEngine,
    IEnumerable<IPersistenceOutboxSource> outboxSources,
    IPersistenceCommitScope commitScope,
    IEnumerable<IRepositoryPersistenceAttemptValidator>? attemptValidators = null)
{
    public async Task<int> SaveChangesAsync(
        DbContext businessDbContext,
        CancellationToken cancellationToken = default)
    {
        var reservedCommitId = commitScope.CurrentCommitId;
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
            if (reservedCommitId.HasValue)
            {
                commitScope.ReleaseCommitId(reservedCommitId.Value);
                throw new InvalidOperationException(
                    "A staged persistence file cannot be confirmed without a database change.");
            }

            return 0;
        }

        var participant = new RepositoryCommitParticipant(
            businessDbContext,
            auditDbContext,
            stagedAuditEntries,
            outboxSource,
            attemptValidators?.ToArray() ?? [],
            requiresBusinessChange: reservedCommitId.HasValue);

        try
        {
            return await commitEngine.CommitAsync(
                $"Repository:{businessDbContext.GetType().Name}",
                participant,
                cancellationToken,
                reservedCommitId);
        }
        finally
        {
            if (reservedCommitId.HasValue)
            {
                commitScope.ReleaseCommitId(reservedCommitId.Value);
            }
        }
    }

    private sealed class RepositoryCommitParticipant(
        DbContext businessDbContext,
        AuditDbContext stagedAuditDbContext,
        IReadOnlyCollection<AuditLogEntry> stagedAuditEntries,
        IPersistenceOutboxSource? outboxSource,
        IReadOnlyCollection<IRepositoryPersistenceAttemptValidator> attemptValidators,
        bool requiresBusinessChange) : IPersistenceCommitParticipant<int>
    {
        public DbContext TransactionOwner => businessDbContext;

        public async Task<PersistenceAttemptResult<int>> PersistAttemptAsync(
            PersistenceAttemptContext context,
            CancellationToken cancellationToken)
        {
            var businessAffectedRows = await businessDbContext.SaveChangesAsync(
                acceptAllChangesOnSuccess: false,
                cancellationToken);
            if (requiresBusinessChange && businessAffectedRows == 0)
            {
                throw new InvalidOperationException(
                    "A staged persistence file requires a committed business row change.");
            }

            var applicableValidators = attemptValidators
                .Where(validator => validator.Supports(businessDbContext))
                .ToArray();
            var hasAgentTaskWrites = businessDbContext.ChangeTracker
                .Entries()
                .Any(entry =>
                    (entry.Entity is AICopilot.Core.AiGateway.Aggregates.AgentTasks.AgentTask or
                        AICopilot.Core.AiGateway.Aggregates.AgentTasks.AgentStep) &&
                    entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
            if (hasAgentTaskWrites && applicableValidators.Length != 1)
            {
                throw new InvalidOperationException(
                    $"AgentTask persistence requires exactly one transaction validator; configured={applicableValidators.Length}.");
            }

            foreach (var validator in applicableValidators)
            {
                await validator.ValidateAsync(businessDbContext, context, cancellationToken);
            }

            var affectedRows = businessAffectedRows;

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

            return new PersistenceAttemptResult<int>(
                affectedRows,
                HasPersistentChanges: affectedRows > 0);
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
