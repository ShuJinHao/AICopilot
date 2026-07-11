using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AICopilot.EntityFrameworkCore.Transactions;

public interface IPersistenceCommitParticipant<TResult>
{
    DbContext TransactionOwner { get; }

    Task<TResult> PersistAttemptAsync(
        PersistenceAttemptContext context,
        CancellationToken cancellationToken);

    void CommitConfirmed(TResult result);
}

public sealed class PersistenceAttemptContext
{
    private readonly DbContext transactionOwner;
    private readonly IDbContextTransaction transaction;

    internal PersistenceAttemptContext(DbContext transactionOwner, IDbContextTransaction transaction)
    {
        this.transactionOwner = transactionOwner;
        this.transaction = transaction;
    }

    public async Task<AuditDbContext> CreateAuditDbContextAsync(
        CancellationToken cancellationToken = default)
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(transactionOwner.Database.GetDbConnection())
            .Options;
        var dbContext = new AuditDbContext(options);
        await dbContext.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
        return dbContext;
    }

    public async Task<OutboxDbContext> CreateOutboxDbContextAsync(
        CancellationToken cancellationToken = default)
    {
        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseNpgsql(transactionOwner.Database.GetDbConnection())
            .Options;
        var dbContext = new OutboxDbContext(options);
        await dbContext.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
        return dbContext;
    }

    internal async Task<PersistenceCommitMarkerDbContext> CreateMarkerDbContextAsync(
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<PersistenceCommitMarkerDbContext>()
            .UseNpgsql(transactionOwner.Database.GetDbConnection())
            .Options;
        var dbContext = new PersistenceCommitMarkerDbContext(options);
        await dbContext.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
        return dbContext;
    }
}

public sealed class PersistenceCommitEngine(
    DbContextOptions<PersistenceCommitMarkerDbContext> markerOptions)
{
    private static readonly TimeSpan VerificationTimeout = TimeSpan.FromSeconds(10);

    public async Task<TResult> CommitAsync<TResult>(
        string operationName,
        IPersistenceCommitParticipant<TResult> participant,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(participant);

        var state = new CommitExecutionState<TResult>(
            Guid.NewGuid(),
            operationName,
            participant,
            cancellationToken);
        var strategy = participant.TransactionOwner.Database.CreateExecutionStrategy();

        var result = await strategy.ExecuteInTransactionAsync(
            state,
            static async (executionState, _) =>
            {
                var transaction = executionState.Participant.TransactionOwner.Database.CurrentTransaction
                                  ?? throw new InvalidOperationException(
                                      "Persistence execution strategy did not create the expected transaction.");
                var attemptContext = new PersistenceAttemptContext(
                    executionState.Participant.TransactionOwner,
                    transaction);

                var attemptResult = await executionState.Participant.PersistAttemptAsync(
                    attemptContext,
                    executionState.CallerCancellationToken);

                await using var markerDbContext = await attemptContext.CreateMarkerDbContextAsync(
                    executionState.CallerCancellationToken);
                markerDbContext.CommitMarkers.Add(
                    new PersistenceCommitMarker(
                        executionState.CommitId,
                        executionState.OperationName,
                        DateTime.UtcNow));
                await markerDbContext.SaveChangesAsync(executionState.CallerCancellationToken);

                return attemptResult;
            },
            (executionState, _) => VerifyCommitAsync(executionState.CommitId, markerOptions),
            CancellationToken.None);

        participant.CommitConfirmed(result);
        return result;
    }

    private static Task<bool> VerifyCommitAsync(
        Guid commitId,
        DbContextOptions<PersistenceCommitMarkerDbContext> options)
    {
        // EF stores the active execution strategy in AsyncLocal state. The verifySucceeded
        // callback still carries the outer strategy, which would suppress retries on this
        // fresh context; isolate that state so verification gets its own retry policy.
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(() => VerifyCommitCoreAsync(commitId, options));
        }
    }

    private static async Task<bool> VerifyCommitCoreAsync(
        Guid commitId,
        DbContextOptions<PersistenceCommitMarkerDbContext> options)
    {
        using var timeout = new CancellationTokenSource(VerificationTimeout);
        try
        {
            await using var verificationContext = new PersistenceCommitMarkerDbContext(options);
            var verificationStrategy = verificationContext.Database.CreateExecutionStrategy();
            return await verificationStrategy.ExecuteAsync(
                token => verificationContext.CommitMarkers
                    .AsNoTracking()
                    .AnyAsync(marker => marker.Id == commitId, token),
                timeout.Token);
        }
        catch (Exception exception)
        {
            throw new PersistenceCommitOutcomeUnknownException(commitId, exception);
        }
    }

    private sealed class CommitExecutionState<TResult>(
        Guid commitId,
        string operationName,
        IPersistenceCommitParticipant<TResult> participant,
        CancellationToken callerCancellationToken)
    {
        public Guid CommitId { get; } = commitId;

        public string OperationName { get; } = operationName;

        public IPersistenceCommitParticipant<TResult> Participant { get; } = participant;

        public CancellationToken CallerCancellationToken { get; } = callerCancellationToken;
    }
}
