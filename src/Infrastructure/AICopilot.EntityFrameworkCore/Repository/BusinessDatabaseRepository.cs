using System.Linq.Expressions;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.EntityFrameworkCore.Specification;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Specification;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

public sealed class BusinessDatabaseRepository(DataAnalysisDbContext dbContext) : IRepository<BusinessDatabase>
{
    public IQueryable<BusinessDatabase> GetQueryable()
    {
        return dbContext.BusinessDatabases.AsQueryable();
    }

    public async Task<List<BusinessDatabase>> ListAsync(
        ISpecification<BusinessDatabase>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).ToListAsync(cancellationToken);
    }

    public async Task<BusinessDatabase?> FirstOrDefaultAsync(
        ISpecification<BusinessDatabase>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> CountAsync(
        ISpecification<BusinessDatabase>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).CountAsync(cancellationToken);
    }

    public async Task<bool> AnyAsync(
        ISpecification<BusinessDatabase>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).AnyAsync(cancellationToken);
    }

    public async Task<BusinessDatabase?> GetByIdAsync<TKey>(
        TKey id,
        CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        return await dbContext.BusinessDatabases.FindAsync([id], cancellationToken);
    }

    public async Task<List<BusinessDatabase>> GetListAsync(
        Expression<Func<BusinessDatabase, bool>> expression,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.BusinessDatabases.Where(expression).ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountAsync(
        Expression<Func<BusinessDatabase, bool>> expression,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.BusinessDatabases.Where(expression).CountAsync(cancellationToken);
    }

    public async Task<BusinessDatabase?> GetAsync(
        Expression<Func<BusinessDatabase, bool>> expression,
        Expression<Func<BusinessDatabase, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplyIncludes(includes).FirstOrDefaultAsync(expression, cancellationToken);
    }

    public async Task<List<BusinessDatabase>> GetListAsync(
        Expression<Func<BusinessDatabase, bool>> expression,
        Expression<Func<BusinessDatabase, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplyIncludes(includes).Where(expression).ToListAsync(cancellationToken);
    }

    public BusinessDatabase Add(BusinessDatabase entity)
    {
        dbContext.BusinessDatabases.Add(entity);
        return entity;
    }

    public void Update(BusinessDatabase entity)
    {
        dbContext.BusinessDatabases.Update(entity);
    }

    public void Delete(BusinessDatabase entity)
    {
        dbContext.BusinessDatabases.Remove(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<BusinessDatabase> ApplySpecification(ISpecification<BusinessDatabase>? specification)
    {
        return SpecificationEvaluator.GetQuery(dbContext.BusinessDatabases.AsQueryable(), specification);
    }

    private IQueryable<BusinessDatabase> ApplyIncludes(Expression<Func<BusinessDatabase, object>>[]? includes)
    {
        var query = dbContext.BusinessDatabases.AsQueryable();
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
