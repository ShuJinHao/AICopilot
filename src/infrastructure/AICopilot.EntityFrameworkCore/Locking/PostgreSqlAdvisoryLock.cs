using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;

namespace AICopilot.EntityFrameworkCore.Locking;

public static class PostgreSqlAdvisoryLock
{
    public static long CreateKey(Guid id)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(id.ToByteArray(), hash);
        return NormalizeKey(BinaryPrimitives.ReadInt64LittleEndian(hash));
    }

    public static long CreateKey(string keySpace, Guid id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keySpace);
        var payload = Encoding.UTF8.GetBytes($"{keySpace.Trim()}:{id:N}");
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(payload, hash);
        return NormalizeKey(BinaryPrimitives.ReadInt64LittleEndian(hash));
    }

    public static long CreateKey(string keySpace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keySpace);
        var payload = Encoding.UTF8.GetBytes(keySpace.Trim());
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(payload, hash);
        return NormalizeKey(BinaryPrimitives.ReadInt64LittleEndian(hash));
    }

    public static async Task<bool> TryAcquireAsync(
        DbConnection connection,
        long key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@lock_key)";
        AddKeyParameter(command, key);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    public static async Task ReleaseAsync(
        DbConnection connection,
        long key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_unlock(@lock_key)";
        AddKeyParameter(command, key);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    public static async Task AcquireTransactionAsync(
        DbConnection connection,
        DbTransaction transaction,
        long key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(connection, transaction.Connection))
        {
            throw new InvalidOperationException(
                "PostgreSQL advisory transaction lock must use the transaction's connection.");
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(@lock_key)";
        AddKeyParameter(command, key);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static void AddKeyParameter(DbCommand command, long key)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lock_key";
        parameter.DbType = DbType.Int64;
        parameter.Value = key;
        command.Parameters.Add(parameter);
    }

    private static long NormalizeKey(long key)
    {
        return key == 0 ? 1 : key;
    }
}
