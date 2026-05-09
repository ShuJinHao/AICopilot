using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

public abstract class EfRepositoryBase<TDbContext, TEntity>(
    TDbContext dbContext,
    AuditTransactionCoordinator transactionCoordinator)
    : EfReadRepositoryBase<TDbContext, TEntity>(dbContext), IRepository<TEntity>
    where TDbContext : DbContext
    where TEntity : class, IEntity, IAggregateRoot
{
    public TEntity Add(TEntity entity)
    {
        DbContext.Set<TEntity>().Add(entity);
        return entity;
    }

    public void Update(TEntity entity)
    {
        DbContext.Set<TEntity>().Update(entity);
    }

    public void Delete(TEntity entity)
    {
        DbContext.Set<TEntity>().Remove(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return transactionCoordinator.SaveChangesAsync(DbContext, cancellationToken);
    }
}
