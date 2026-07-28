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
    private const string NormalizedUserNameKeySpace = "AICopilot.Identity.NormalizedUserName.v1";

    public async Task AcquireAsync(
        ExternalIdentityBindingInvariantScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.ExternalUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.NormalizedUserName);
        ArgumentNullException.ThrowIfNull(scope.KnownUserIds);

        var currentTransaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "The external identity binding invariant must be acquired inside the Identity transaction.");
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "The Identity transaction connection must be open before acquiring the external identity binding invariant.");
        }

        var provider = scope.Provider.Trim();
        var keys = new HashSet<long>
        {
            PostgreSqlAdvisoryLock.CreateKey(
                $"{ExternalIdentityKeySpace}:{provider}:{scope.TenantId.Trim()}:{scope.ExternalUserId.Trim()}"),
            PostgreSqlAdvisoryLock.CreateKey(
                $"{NormalizedUserNameKeySpace}:{scope.NormalizedUserName.Trim()}")
        };
        foreach (var userId in scope.KnownUserIds.Where(userId => userId != Guid.Empty))
        {
            keys.Add(PostgreSqlAdvisoryLock.CreateKey(
                $"{UserProviderKeySpace}:{provider}:{userId:N}"));
        }

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
