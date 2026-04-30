using System.Linq.Expressions;
using AICopilot.EntityFrameworkCore.Specification;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Specification;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

public sealed class RagRepository<T>(RagDbContext dbContext) : IRepository<T>
    where T : class, IEntity, IAggregateRoot
{
    public async Task<List<T>> ListAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).ToListAsync(cancellationToken);
    }

    public async Task<T?> FirstOrDefaultAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> CountAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).CountAsync(cancellationToken);
    }

    public async Task<bool> AnyAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).AnyAsync(cancellationToken);
    }

    public async Task<T?> GetByIdAsync<TKey>(
        TKey id,
        CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        return await dbContext.Set<T>().FindAsync([id], cancellationToken);
    }

    public async Task<List<T>> GetListAsync(
        Expression<Func<T, bool>> expression,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Set<T>().Where(expression).ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountAsync(
        Expression<Func<T, bool>> expression,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Set<T>().Where(expression).CountAsync(cancellationToken);
    }

    public async Task<T?> GetAsync(
        Expression<Func<T, bool>> expression,
        Expression<Func<T, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplyIncludes(includes).FirstOrDefaultAsync(expression, cancellationToken);
    }

    public async Task<List<T>> GetListAsync(
        Expression<Func<T, bool>> expression,
        Expression<Func<T, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplyIncludes(includes).Where(expression).ToListAsync(cancellationToken);
    }

    public T Add(T entity)
    {
        dbContext.Set<T>().Add(entity);
        return entity;
    }

    public void Update(T entity)
    {
        dbContext.Set<T>().Update(entity);
    }

    public void Delete(T entity)
    {
        dbContext.Set<T>().Remove(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<T> ApplySpecification(ISpecification<T>? specification)
    {
        return SpecificationEvaluator.GetQuery(dbContext.Set<T>().AsQueryable(), specification);
    }

    private IQueryable<T> ApplyIncludes(Expression<Func<T, object>>[]? includes)
    {
        var query = dbContext.Set<T>().AsQueryable();
        if (includes is null)
        {
            return query;
        }

        foreach (var include in includes)
        {
            query = query.Include(include);
        }

        return query;
    }
}
