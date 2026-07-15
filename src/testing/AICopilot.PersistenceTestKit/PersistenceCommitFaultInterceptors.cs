using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AICopilot.PersistenceTestKit;

internal class BusinessSaveCounterInterceptor : SaveChangesInterceptor
{
    public int SaveAttemptCount { get; protected set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context?.ChangeTracker.HasChanges() == true)
        {
            SaveAttemptCount++;
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

internal sealed class FailFirstBusinessSaveInterceptor : BusinessSaveCounterInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        base.SavingChangesAsync(eventData, result, cancellationToken);
        if (SaveAttemptCount == 1)
        {
            throw PersistenceTestFailure.Transient("Simulated pre-commit transient failure.");
        }

        return ValueTask.FromResult(result);
    }
}

internal sealed class KnownBusinessSaveFailureInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context?.ChangeTracker.HasChanges() == true)
        {
            throw new InvalidOperationException("Simulated known business save failure.");
        }

        return ValueTask.FromResult(result);
    }
}

internal sealed class CommitAcknowledgementLostInterceptor : DbTransactionInterceptor
{
    private int remainingFaults = 1;

    public int ThrowCount { get; private set; }

    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref remainingFaults, 0) == 1)
        {
            ThrowCount++;
            throw PersistenceTestFailure.Transient("Simulated commit acknowledgement loss.");
        }

        return Task.CompletedTask;
    }
}

internal sealed class CancelCallerAtCommitInterceptor(CancellationTokenSource callerCancellation)
    : DbTransactionInterceptor
{
    public int CommitAttemptCount { get; private set; }

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        CommitAttemptCount++;
        callerCancellation.Cancel();
        return ValueTask.FromResult(result);
    }
}

internal sealed class FailMarkerQueryInterceptor(int remainingFailures) : DbConnectionInterceptor
{
    private int remainingFailures = remainingFailures;

    public int QueryAttemptCount { get; private set; }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        QueryAttemptCount++;
        if (DecrementIfPositive(ref remainingFailures))
        {
            throw PersistenceTestFailure.Transient("Simulated marker verification outage.");
        }

        return ValueTask.FromResult(result);
    }

    private static bool DecrementIfPositive(ref int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref value);
            if (current <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref value, current - 1, current) == current)
            {
                return true;
            }
        }
    }
}

internal static class PersistenceTestFailure
{
    public static NpgsqlException Transient(string message)
        => new(message, new TimeoutException(message));
}
