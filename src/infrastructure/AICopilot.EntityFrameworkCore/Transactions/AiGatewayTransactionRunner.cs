using Microsoft.EntityFrameworkCore;
using AICopilot.Services.Contracts;

namespace AICopilot.EntityFrameworkCore.Transactions;

internal readonly record struct AiGatewayTransactionAttempt<TResult>(
    TResult Result,
    int DirectAffectedRows = 0);

internal sealed class AiGatewayTransactionRunner(
    DbContextOptions<AiGatewayDbContext> dbContextOptions,
    PersistenceCommitEngine commitEngine,
    IPersistenceCommitScope commitScope)
{
    public async Task<TResult> ExecuteAsync<TResult>(
        string operationName,
        Func<AiGatewayDbContext, CancellationToken, Task<AiGatewayTransactionAttempt<TResult>>> action,
        CancellationToken cancellationToken)
    {
        var reservedCommitId = commitScope.CurrentCommitId;
        try
        {
            await using var isolatedDbContext = new AiGatewayDbContext(dbContextOptions);
            return await commitEngine.CommitAsync(
                operationName,
                new Participant<TResult>(isolatedDbContext, action),
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
        Func<AiGatewayDbContext, CancellationToken, Task<AiGatewayTransactionAttempt<TResult>>> action)
        : IPersistenceCommitParticipant<TResult>
    {
        private AiGatewayTransactionAttempt<TResult> attempt;
        private int attemptCount;

        public DbContext TransactionOwner => dbContext;

        public async Task<PersistenceAttemptResult<TResult>> PersistAttemptAsync(
            PersistenceAttemptContext context,
            CancellationToken cancellationToken)
        {
            _ = context;
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
            _ = result;
            dbContext.ChangeTracker.AcceptAllChanges();
        }
    }
}
