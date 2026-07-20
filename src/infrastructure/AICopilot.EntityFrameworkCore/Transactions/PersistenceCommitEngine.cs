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

    Task<PersistenceAttemptResult<TResult>> PersistAttemptAsync(
        PersistenceAttemptContext context,
        CancellationToken cancellationToken);

    void CommitConfirmed(TResult result);
}

public readonly record struct PersistenceAttemptResult<TResult>(
    TResult Result,
    bool HasPersistentChanges);

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

    public async Task<AiGatewayDbContext> CreateAiGatewayDbContextAsync(
        CancellationToken cancellationToken = default)
    {
        var options = new DbContextOptionsBuilder<AiGatewayDbContext>()
            .UseNpgsql(transactionOwner.Database.GetDbConnection())
            .Options;
        var dbContext = new AiGatewayDbContext(options);
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
        CancellationToken cancellationToken = default,
        Guid? commitId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(participant);
        if (commitId == Guid.Empty)
        {
            throw new ArgumentException("Persistence commit id cannot be empty.", nameof(commitId));
        }

        var state = new CommitExecutionState<TResult>(
            commitId ?? Guid.NewGuid(),
            operationName,
            participant,
            cancellationToken);
        var strategy = participant.TransactionOwner.Database.CreateExecutionStrategy();

        var attemptResult = await strategy.ExecuteInTransactionAsync(
            state,
            static async (executionState, _) =>
            {
                executionState.BeginAttempt();
                var transaction = executionState.Participant.TransactionOwner.Database.CurrentTransaction
                                  ?? throw new InvalidOperationException(
                                      "Persistence execution strategy did not create the expected transaction.");
                var attemptContext = new PersistenceAttemptContext(
                    executionState.Participant.TransactionOwner,
                    transaction);

                var currentAttempt = await executionState.Participant.PersistAttemptAsync(
                    attemptContext,
                    executionState.CallerCancellationToken);

                executionState.SetMarkerExpectation(currentAttempt.HasPersistentChanges);
                if (!currentAttempt.HasPersistentChanges)
                {
                    return currentAttempt;
                }

                await using var markerDbContext = await attemptContext.CreateMarkerDbContextAsync(
                    executionState.CallerCancellationToken);
                markerDbContext.CommitMarkers.Add(
                    new PersistenceCommitMarker(
                        executionState.CommitId,
                        executionState.OperationName,
                        DateTime.UtcNow));
                await markerDbContext.SaveChangesAsync(executionState.CallerCancellationToken);

                return currentAttempt;
            },
            (executionState, _) => executionState.VerifySucceededAsync(markerOptions),
            CancellationToken.None);

        participant.CommitConfirmed(attemptResult.Result);
        return attemptResult.Result;
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

        private MarkerExpectation markerExpectation;

        public void BeginAttempt()
        {
            markerExpectation = MarkerExpectation.Undetermined;
        }

        public void SetMarkerExpectation(bool hasPersistentChanges)
        {
            markerExpectation = hasPersistentChanges
                ? MarkerExpectation.Required
                : MarkerExpectation.NotRequired;
        }

        public Task<bool> VerifySucceededAsync(
            DbContextOptions<PersistenceCommitMarkerDbContext> options)
        {
            return markerExpectation switch
            {
                MarkerExpectation.NotRequired => Task.FromResult(true),
                MarkerExpectation.Required => VerifyCommitAsync(CommitId, options),
                _ => Task.FromResult(false)
            };
        }
    }

    private enum MarkerExpectation
    {
        Undetermined,
        NotRequired,
        Required
    }
}
