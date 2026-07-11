using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Transactions;

public sealed class IdentityTransactionalExecutionService(
    IdentityStoreDbContext dbContext,
    PersistenceCommitEngine commitEngine) : ITransactionalExecutionService
{
    public Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        if (typeof(IResult).IsAssignableFrom(typeof(TResult)))
        {
            throw new InvalidOperationException(
                "Identity operations returning Result must use ExecuteResultAsync so rejected outcomes roll back.");
        }

        return ExecuteCoreAsync(operation, shouldCommit: null, cancellationToken);
    }

    public Task<Result<TValue>> ExecuteResultAsync<TValue>(
        Func<CancellationToken, Task<Result<TValue>>> operation,
        CancellationToken cancellationToken = default)
    {
        return ExecuteCoreAsync(
            operation,
            static result => result.IsSuccess,
            cancellationToken);
    }

    public Task<Result> ExecuteResultAsync(
        Func<CancellationToken, Task<Result>> operation,
        CancellationToken cancellationToken = default)
    {
        return ExecuteCoreAsync(
            operation,
            static result => result.IsSuccess,
            cancellationToken);
    }

    private async Task<TResult> ExecuteCoreAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        Func<TResult, bool>? shouldCommit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            return await commitEngine.CommitAsync(
                "Identity:Transaction",
                new IdentityCommitParticipant<TResult>(dbContext, operation, shouldCommit),
                cancellationToken);
        }
        catch (IdentityOperationRejectedException<TResult> exception)
        {
            return exception.Result;
        }
    }

    private sealed class IdentityCommitParticipant<TResult>(
        IdentityStoreDbContext dbContext,
        Func<CancellationToken, Task<TResult>> operation,
        Func<TResult, bool>? shouldCommit)
        : IPersistenceCommitParticipant<TResult>
    {
        private int attemptCount;

        public DbContext TransactionOwner => dbContext;

        public async Task<PersistenceAttemptResult<TResult>> PersistAttemptAsync(
            PersistenceAttemptContext context,
            CancellationToken cancellationToken)
        {
            var currentAttempt = Interlocked.Increment(ref attemptCount);
            dbContext.BeginPersistenceAttempt(clearTrackedState: currentAttempt > 1);

            try
            {
                var result = await operation(cancellationToken);
                if (shouldCommit is not null && !shouldCommit(result))
                {
                    throw new IdentityOperationRejectedException<TResult>(result);
                }

                await dbContext.SaveChangesAsync(
                    acceptAllChangesOnSuccess: false,
                    cancellationToken);
                return new PersistenceAttemptResult<TResult>(
                    result,
                    dbContext.HasPersistedChangesInCurrentAttempt);
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

    private sealed class IdentityOperationRejectedException<TResult>(TResult result) : Exception
    {
        public TResult Result { get; } = result;
    }
}
