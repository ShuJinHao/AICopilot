using System.Data;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AICopilot.EntityFrameworkCore.Locking;

public sealed class PostgresIdentityEnabledAdminInvariantGuard(
    IdentityStoreDbContext dbContext) : IIdentityEnabledAdminInvariantGuard
{
    public const string ResourceName = "AICopilot.Identity.EnabledAdminInvariant.v1";

    public static readonly long ResourceKey = PostgreSqlAdvisoryLock.CreateKey(ResourceName);

    public async Task AcquireAsync(CancellationToken cancellationToken = default)
    {
        var currentTransaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "The enabled Admin invariant must be acquired inside the Identity transaction.");
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "The Identity transaction connection must be open before acquiring the enabled Admin invariant.");
        }

        await PostgreSqlAdvisoryLock.AcquireTransactionAsync(
            connection,
            currentTransaction.GetDbTransaction(),
            ResourceKey,
            cancellationToken);
    }
}
