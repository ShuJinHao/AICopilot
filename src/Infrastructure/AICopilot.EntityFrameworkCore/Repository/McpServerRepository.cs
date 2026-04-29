using System.Linq.Expressions;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.EntityFrameworkCore.Specification;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Specification;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

public sealed class McpServerRepository(McpServerDbContext dbContext) : IRepository<McpServerInfo>
{
    public IQueryable<McpServerInfo> GetQueryable()
    {
        return dbContext.McpServerInfos.AsQueryable();
    }

    public async Task<List<McpServerInfo>> ListAsync(
        ISpecification<McpServerInfo>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).ToListAsync(cancellationToken);
    }

    public async Task<McpServerInfo?> FirstOrDefaultAsync(
        ISpecification<McpServerInfo>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> CountAsync(
        ISpecification<McpServerInfo>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).CountAsync(cancellationToken);
    }

    public async Task<bool> AnyAsync(
        ISpecification<McpServerInfo>? specification = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(specification).AnyAsync(cancellationToken);
    }

    public async Task<McpServerInfo?> GetByIdAsync<TKey>(
        TKey id,
        CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        return await dbContext.McpServerInfos.FindAsync([id], cancellationToken);
    }

    public async Task<List<McpServerInfo>> GetListAsync(
        Expression<Func<McpServerInfo, bool>> expression,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.McpServerInfos.Where(expression).ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountAsync(
        Expression<Func<McpServerInfo, bool>> expression,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.McpServerInfos.Where(expression).CountAsync(cancellationToken);
    }

    public async Task<McpServerInfo?> GetAsync(
        Expression<Func<McpServerInfo, bool>> expression,
        Expression<Func<McpServerInfo, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplyIncludes(includes).FirstOrDefaultAsync(expression, cancellationToken);
    }

    public async Task<List<McpServerInfo>> GetListAsync(
        Expression<Func<McpServerInfo, bool>> expression,
        Expression<Func<McpServerInfo, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplyIncludes(includes).Where(expression).ToListAsync(cancellationToken);
    }

    public McpServerInfo Add(McpServerInfo entity)
    {
        dbContext.McpServerInfos.Add(entity);
        return entity;
    }

    public void Update(McpServerInfo entity)
    {
        dbContext.McpServerInfos.Update(entity);
    }

    public void Delete(McpServerInfo entity)
    {
        dbContext.McpServerInfos.Remove(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<McpServerInfo> ApplySpecification(ISpecification<McpServerInfo>? specification)
    {
        return SpecificationEvaluator.GetQuery(dbContext.McpServerInfos.AsQueryable(), specification);
    }

    private IQueryable<McpServerInfo> ApplyIncludes(Expression<Func<McpServerInfo, object>>[]? includes)
    {
        var query = dbContext.McpServerInfos.AsQueryable();
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
