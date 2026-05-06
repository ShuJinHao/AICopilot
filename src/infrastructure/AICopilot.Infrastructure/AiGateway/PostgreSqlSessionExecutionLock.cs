using System.Security.Cryptography;
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
        var key = BuildLockKey(sessionId);
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(AcquireTimeout);

            while (true)
            {
                if (await TryAcquireAsync(connection, key, timeoutCts.Token))
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

    private static async Task<bool> TryAcquireAsync(
        NpgsqlConnection connection,
        long key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@key);";
        command.Parameters.AddWithValue("key", key);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static long BuildLockKey(Guid sessionId)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(sessionId.ToByteArray(), hash);
        var key = BitConverter.ToInt64(hash[..8]);
        return key == 0 ? 1 : key;
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
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock(@key);";
                command.Parameters.AddWithValue("key", key);
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to release PostgreSQL advisory session execution lock.");
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
