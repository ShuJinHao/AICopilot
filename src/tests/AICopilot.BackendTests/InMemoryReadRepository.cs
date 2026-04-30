using System.Linq.Expressions;
using AICopilot.EntityFrameworkCore.Specification;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.BackendTests;

internal sealed class InMemoryReadRepository<T>(IReadOnlyCollection<T> items) : IReadRepository<T>
    where T : class, IAggregateRoot
{
    public InMemoryReadRepository()
        : this([])
    {
    }

    public Task<List<T>> ListAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(SpecificationEvaluator.GetQuery(items.AsQueryable(), specification).ToList());
    }

    public Task<T?> FirstOrDefaultAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(SpecificationEvaluator.GetQuery(items.AsQueryable(), specification).FirstOrDefault());
    }

    public Task<int> CountAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(SpecificationEvaluator.GetQuery(items.AsQueryable(), specification).Count());
    }

    public Task<bool> AnyAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(SpecificationEvaluator.GetQuery(items.AsQueryable(), specification).Any());
    }

    public Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        return Task.FromResult(items.FirstOrDefault(item => Equals(GetId(item), id)));
    }

    public Task<List<T>> GetListAsync(Expression<Func<T, bool>> expression, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(items.AsQueryable().Where(expression).ToList());
    }

    public Task<int> GetCountAsync(Expression<Func<T, bool>> expression, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(items.AsQueryable().Count(expression));
    }

    public Task<T?> GetAsync(
        Expression<Func<T, bool>> expression,
        Expression<Func<T, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(items.AsQueryable().FirstOrDefault(expression));
    }

    public Task<List<T>> GetListAsync(
        Expression<Func<T, bool>> expression,
        Expression<Func<T, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(items.AsQueryable().Where(expression).ToList());
    }

    private static object? GetId(T item)
    {
        return typeof(T).GetProperty("Id")?.GetValue(item);
    }
}
