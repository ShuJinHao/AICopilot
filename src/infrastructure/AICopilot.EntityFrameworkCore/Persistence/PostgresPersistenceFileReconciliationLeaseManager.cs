using AICopilot.EntityFrameworkCore.Locking;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace AICopilot.EntityFrameworkCore.Persistence;

public sealed class PostgresPersistenceFileReconciliationLeaseManager(
    DbContextOptions<PersistenceCommitMarkerDbContext> options)
    : IPersistenceFileReconciliationLeaseManager
{
    public async Task<IPersistenceFileReconciliationLease?> TryAcquireAsync(
        Guid commitId,
        CancellationToken cancellationToken = default)
    {
        if (commitId == Guid.Empty)
        {
            throw new ArgumentException("Persistence commit id is required.", nameof(commitId));
        }

        var lockKey = PostgreSqlAdvisoryLock.CreateKey("persistence-file-reconciliation", commitId);
        var dbContext = new PersistenceCommitMarkerDbContext(options);
        try
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
            var acquired = await PostgreSqlAdvisoryLock.TryAcquireAsync(
                dbContext.Database.GetDbConnection(),
                lockKey,
                cancellationToken);
            if (!acquired)
            {
                await dbContext.DisposeAsync();
                return null;
            }

            return new PostgresReconciliationLease(dbContext, lockKey);
        }
        catch
        {
            await dbContext.DisposeAsync();
            throw;
        }
    }

    private sealed class PostgresReconciliationLease(
        PersistenceCommitMarkerDbContext dbContext,
        long lockKey) : IPersistenceFileReconciliationLease
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (dbContext.Database.GetDbConnection().State == ConnectionState.Open)
                {
                    await PostgreSqlAdvisoryLock.ReleaseAsync(
                        dbContext.Database.GetDbConnection(),
                        lockKey,
                        CancellationToken.None);
                }
            }
            finally
            {
                await dbContext.DisposeAsync();
            }
        }
    }
}
