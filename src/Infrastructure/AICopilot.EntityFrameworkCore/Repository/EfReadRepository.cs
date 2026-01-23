using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.EntityFrameworkCore.Repository;

public class EfReadRepository<T>(AiCopilotDbContext dbContext) : IReadRepository<T>
    where T : class, IAggregateRoot
{
    public IQueryable<T> GetQueryable()
    {
        return dbContext.Set<T>().AsQueryable();
    }

    public async Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        return await dbContext.Set<T>().FindAsync([id], cancellationToken);
    }

    public async Task<List<T>> GetListAsync(Expression<Func<T, bool>> expression,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Set<T>().Where(expression).ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountAsync(Expression<Func<T, bool>> expression,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Set<T>().Where(expression).CountAsync(cancellationToken);
    }
}