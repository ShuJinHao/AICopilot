using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.EntityFrameworkCore.Repository;

public class EfRepository<T>(
    AiCopilotDbContext dbContext,
    AuditDbContext auditDbContext) : EfReadRepository<T>(dbContext), IRepository<T>
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

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _dbContext.SaveChangesAsync(cancellationToken);
        return result + await auditDbContext.SaveChangesAsync(cancellationToken);
    }
}
