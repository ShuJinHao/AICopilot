using System.Data;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AICopilot.EntityFrameworkCore.Locking;

public sealed class PostgresExternalIdentityBindingInvariantGuard(
    IdentityStoreDbContext dbContext) : IExternalIdentityBindingInvariantGuard
{
    private const string ExternalIdentityKeySpace = "AICopilot.Identity.ExternalBinding.v1";
    private const string UserProviderKeySpace = "AICopilot.Identity.UserProviderBinding.v1";

    public async Task AcquireAsync(
        string provider,
        string tenantId,
        string externalUserId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalUserId);

        var currentTransaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "The external identity binding invariant must be acquired inside the Identity transaction.");
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "The Identity transaction connection must be open before acquiring the external identity binding invariant.");
        }

        var keys = new[]
        {
            PostgreSqlAdvisoryLock.CreateKey(
                $"{ExternalIdentityKeySpace}:{provider.Trim()}:{tenantId.Trim()}:{externalUserId.Trim()}"),
            PostgreSqlAdvisoryLock.CreateKey(
                $"{UserProviderKeySpace}:{provider.Trim()}:{userId:N}")
        };

        foreach (var key in keys.Order())
        {
            await PostgreSqlAdvisoryLock.AcquireTransactionAsync(
                connection,
                currentTransaction.GetDbTransaction(),
                key,
                cancellationToken);
        }
    }
}
