using System.Linq.Expressions;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.HarnessTestKit;

internal sealed class InMemoryRepository<T>(params T[] initialItems) : IRepository<T>
    where T : class, IEntity, IAggregateRoot
{
    public List<T> Items { get; } = [.. initialItems];

    public T Add(T entity)
    {
        Items.Add(entity);
        return entity;
    }

    public void Update(T entity)
    {
        if (!Items.Contains(entity))
        {
            Items.Add(entity);
        }
    }

    public void Delete(T entity) => Items.Remove(entity);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(1);

    public Task<List<T>> ListAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Apply(specification).ToList());

    public Task<T?> FirstOrDefaultAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Apply(specification).FirstOrDefault());

    public Task<int> CountAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Apply(specification).Count());

    public Task<bool> AnyAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Apply(specification).Any());

    public Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
        where TKey : notnull =>
        Task.FromResult(Items.FirstOrDefault(item => Equals(GetId(item), id)));

    public Task<List<T>> GetListAsync(
        Expression<Func<T, bool>> expression,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.AsQueryable().Where(expression).ToList());

    public Task<int> GetCountAsync(
        Expression<Func<T, bool>> expression,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.AsQueryable().Count(expression));

    public Task<T?> GetAsync(
        Expression<Func<T, bool>> expression,
        Expression<Func<T, object>>[]? includes = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.AsQueryable().FirstOrDefault(expression));

    public Task<List<T>> GetListAsync(
        Expression<Func<T, bool>> expression,
        Expression<Func<T, object>>[]? includes = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.AsQueryable().Where(expression).ToList());

    private IQueryable<T> Apply(ISpecification<T>? specification) =>
        TestSpecificationEvaluator.Apply(Items.AsQueryable(), specification);

    private static object? GetId(T item) => typeof(T).GetProperty("Id")?.GetValue(item);
}
