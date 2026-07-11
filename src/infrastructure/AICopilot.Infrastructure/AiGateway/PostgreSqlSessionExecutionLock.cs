using AICopilot.EntityFrameworkCore.Locking;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AICopilot.Infrastructure.AiGateway;

public sealed class PostgreSqlSessionExecutionLock(
    string connectionString,
    ILogger<PostgreSqlSessionExecutionLock> logger) : ISessionExecutionLock
{
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var key = PostgreSqlAdvisoryLock.CreateKey(sessionId);
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(AcquireTimeout);

            while (true)
            {
                if (await PostgreSqlAdvisoryLock.TryAcquireAsync(connection, key, timeoutCts.Token))
                {
                    return new Releaser(connection, key, logger);
                }

                await Task.Delay(RetryDelay, timeoutCts.Token);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Releaser(
        NpgsqlConnection connection,
        long key,
        ILogger logger) : IAsyncDisposable
    {
        private int _released;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            try
            {
                await PostgreSqlAdvisoryLock.ReleaseAsync(connection, key);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    "Failed to release PostgreSQL advisory session execution lock. ErrorType={ErrorType}; OriginalMessage=hidden_by_security_policy",
                    ex.GetType().Name);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
