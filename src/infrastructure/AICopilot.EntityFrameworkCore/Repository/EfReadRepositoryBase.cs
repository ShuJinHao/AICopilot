using System.Linq.Expressions;
using AICopilot.EntityFrameworkCore.Specification;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Specification;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

public abstract class EfReadRepositoryBase<TDbContext, TEntity>(TDbContext dbContext) : IReadRepository<TEntity>
    where TDbContext : DbContext
    where TEntity : class, IAggregateRoot
{
    protected TDbContext DbContext { get; } = dbContext;

    public async Task<List<TEntity>> ListAsync(
        ISpecification<TEntity>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).ToListAsync(cancellationToken);
    }

    public async Task<TEntity?> FirstOrDefaultAsync(
        ISpecification<TEntity>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> CountAsync(
        ISpecification<TEntity>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).CountAsync(cancellationToken);
    }

    public async Task<bool> AnyAsync(
        ISpecification<TEntity>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).AnyAsync(cancellationToken);
    }

    public async Task<TEntity?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        return await DbContext.Set<TEntity>().FindAsync([id], cancellationToken);
    }

    public async Task<List<TEntity>> GetListAsync(
        Expression<Func<TEntity, bool>> expression,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<TEntity>().Where(expression).ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountAsync(
        Expression<Func<TEntity, bool>> expression,
        CancellationToken cancellationToken = default)
    {
        return await DbContext.Set<TEntity>().Where(expression).CountAsync(cancellationToken);
    }

    public async Task<TEntity?> GetAsync(
        Expression<Func<TEntity, bool>> expression,
        Expression<Func<TEntity, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplyIncludes(includes).FirstOrDefaultAsync(expression, cancellationToken);
    }

    public async Task<List<TEntity>> GetListAsync(
        Expression<Func<TEntity, bool>> expression,
        Expression<Func<TEntity, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplyIncludes(includes).Where(expression).ToListAsync(cancellationToken);
    }

    protected IQueryable<TEntity> ApplySpecification(ISpecification<TEntity>? specification)
    {
        return SpecificationEvaluator.GetQuery(DbContext.Set<TEntity>().AsQueryable(), specification);
    }

    protected IQueryable<TEntity> ApplyIncludes(Expression<Func<TEntity, object>>[]? includes)
    {
        var query = DbContext.Set<TEntity>().AsQueryable();
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
