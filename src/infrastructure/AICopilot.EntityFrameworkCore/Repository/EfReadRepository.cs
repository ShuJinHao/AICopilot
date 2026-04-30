using System.Linq.Expressions;
using AICopilot.EntityFrameworkCore.Specification;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Specification;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

public class EfReadRepository<T>(AiCopilotDbContext dbContext) : IReadRepository<T>
    where T : class, IAggregateRoot
{
    public async Task<List<T>> ListAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await SpecificationEvaluator
            .GetQuery(dbContext.Set<T>().AsQueryable(), specification)
            .ToListAsync(cancellationToken);
    }

    public async Task<T?> FirstOrDefaultAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await SpecificationEvaluator
            .GetQuery(dbContext.Set<T>().AsQueryable(), specification)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> CountAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await SpecificationEvaluator
            .GetQuery(dbContext.Set<T>().AsQueryable(), specification)
            .CountAsync(cancellationToken);
    }

    public async Task<bool> AnyAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await SpecificationEvaluator
            .GetQuery(dbContext.Set<T>().AsQueryable(), specification)
            .AnyAsync(cancellationToken);
    }

    public async Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
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
        var query = dbContext.Set<T>().AsQueryable();

        if (includes is not null)
        {
            foreach (var include in includes)
            {
                query = query.Include(include);
            }
        }

        return await query.FirstOrDefaultAsync(expression, cancellationToken);
    }

    public async Task<List<T>> GetListAsync(
        Expression<Func<T, bool>> expression,
        Expression<Func<T, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Set<T>().AsQueryable();

        if (includes is not null)
        {
            foreach (var include in includes)
            {
                query = query.Include(include);
            }
        }

        return await query.Where(expression).ToListAsync(cancellationToken);
    }
}
