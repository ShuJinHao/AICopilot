using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Transactions;

internal readonly record struct AgentExecutionTransactionAttempt<TResult>(
    TResult Result,
    int DirectAffectedRows = 0);

internal sealed class AgentExecutionTransactionRunner(
    DbContextOptions<AiGatewayDbContext> dbContextOptions,
    PersistenceCommitEngine commitEngine,
    IPersistenceCommitScope commitScope,
    IEnumerable<IRepositoryPersistenceAttemptValidator> attemptValidators)
{
    public async Task<TResult> ExecuteAsync<TResult>(
        string operationName,
        Func<AiGatewayDbContext, CancellationToken, Task<AgentExecutionTransactionAttempt<TResult>>> action,
        CancellationToken cancellationToken)
    {
        var reservedCommitId = commitScope.CurrentCommitId;
        try
        {
            await using var isolatedDbContext = new AiGatewayDbContext(dbContextOptions);
            return await commitEngine.CommitAsync(
                operationName,
                new Participant<TResult>(
                    isolatedDbContext,
                    action,
                    attemptValidators.ToArray()),
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

    private sealed class Participant<TResult>(
        AiGatewayDbContext dbContext,
        Func<AiGatewayDbContext, CancellationToken, Task<AgentExecutionTransactionAttempt<TResult>>> action,
        IReadOnlyCollection<IRepositoryPersistenceAttemptValidator> attemptValidators)
        : IPersistenceCommitParticipant<TResult>
    {
        private AgentExecutionTransactionAttempt<TResult> attempt;
        private int attemptCount;

        public DbContext TransactionOwner => dbContext;

        public async Task<PersistenceAttemptResult<TResult>> PersistAttemptAsync(
            PersistenceAttemptContext context,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref attemptCount) > 1)
            {
                dbContext.ChangeTracker.Clear();
            }

            try
            {
                attempt = await action(dbContext, cancellationToken);
                var affectedRows = await dbContext.SaveChangesAsync(
                    acceptAllChangesOnSuccess: false,
                    cancellationToken);

                var hasAgentTaskWrites = dbContext.ChangeTracker
                    .Entries()
                    .Any(entry =>
                        entry.Entity is AgentTask or AgentStep &&
                        entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
                if (hasAgentTaskWrites)
                {
                    var applicableValidators = attemptValidators
                        .Where(validator => validator.Supports(dbContext))
                        .ToArray();
                    if (applicableValidators.Length != 1)
                    {
                        throw new InvalidOperationException(
                            $"AgentTask execution transaction requires exactly one validator; configured={applicableValidators.Length}.");
                    }

                    foreach (var validator in applicableValidators)
                    {
                        await validator.ValidateAsync(dbContext, context, cancellationToken);
                    }
                }

                var totalAffectedRows = checked(affectedRows + attempt.DirectAffectedRows);
                return new PersistenceAttemptResult<TResult>(
                    attempt.Result,
                    HasPersistentChanges: totalAffectedRows > 0);
            }
            catch
            {
                dbContext.ChangeTracker.Clear();
                throw;
            }
        }

        public void CommitConfirmed(TResult result)
        {
            dbContext.ChangeTracker.AcceptAllChanges();
        }
    }
}
