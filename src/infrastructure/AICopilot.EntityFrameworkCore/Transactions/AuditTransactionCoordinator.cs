using AICopilot.EntityFrameworkCore.AuditLogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AICopilot.EntityFrameworkCore.Transactions;

public sealed class AuditTransactionCoordinator(AuditDbContext auditDbContext)
{
    public Task<int> SaveChangesAsync(DbContext businessDbContext, CancellationToken cancellationToken = default)
    {
        var stagedAuditEntries = auditDbContext.ChangeTracker
            .Entries<AuditLogEntry>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToArray();

        var strategy = businessDbContext.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await businessDbContext.Database.BeginTransactionAsync(cancellationToken);
            var committed = false;
            try
            {
                var result = await businessDbContext.SaveChangesAsync(cancellationToken);

                if (stagedAuditEntries.Length > 0)
                {
                    var auditOptions = new DbContextOptionsBuilder<AuditDbContext>()
                        .UseNpgsql(businessDbContext.Database.GetDbConnection())
                        .Options;

                    await using var transactionalAuditDbContext = new AuditDbContext(auditOptions);
                    await transactionalAuditDbContext.Database.UseTransactionAsync(
                        transaction.GetDbTransaction(),
                        cancellationToken);

                    transactionalAuditDbContext.AuditLogs.AddRange(stagedAuditEntries);
                    result += await transactionalAuditDbContext.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                committed = true;

                foreach (var entry in auditDbContext.ChangeTracker.Entries<AuditLogEntry>())
                {
                    if (stagedAuditEntries.Contains(entry.Entity))
                    {
                        entry.State = EntityState.Detached;
                    }
                }

                return result;
            }
            finally
            {
                if (!committed && transaction.GetDbTransaction().Connection is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
            }
        });
    }
}
