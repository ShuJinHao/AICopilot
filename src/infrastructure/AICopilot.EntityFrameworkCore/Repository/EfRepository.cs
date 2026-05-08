using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.EntityFrameworkCore.Repository;

public class EfRepository<T>(
    AiCopilotDbContext dbContext,
    AuditTransactionCoordinator transactionCoordinator) : EfReadRepository<T>(dbContext), IRepository<T>
    where T : class, IEntity, IAggregateRoot
{
    private readonly AiCopilotDbContext _dbContext = dbContext;

    public T Add(T entity)
    {
        _dbContext.Set<T>().Add(entity);
        return entity;
    }

    public void Update(T entity)
    {
        _dbContext.Set<T>().Update(entity);
    }

    public void Delete(T entity)
    {
        _dbContext.Set<T>().Remove(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return transactionCoordinator.SaveChangesAsync(_dbContext, cancellationToken);
    }
}
