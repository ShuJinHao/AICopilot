using AICopilot.SharedKernel.Domain;

namespace AICopilot.SharedKernel.Specification;

public static class SpecificationQueryEvaluator
{
    public static IQueryable<T> GetQuery<T>(IQueryable<T> inputQuery, ISpecification<T>? specification)
        where T : class, IEntity
    {
        if (specification is null)
        {
            return inputQuery;
        }

        var query = inputQuery;
        if (specification.FilterCondition is not null)
        {
            query = query.Where(specification.FilterCondition);
        }

        if (specification.OrderBy is not null)
        {
            query = query.OrderBy(specification.OrderBy);
        }
        else if (specification.OrderByDescending is not null)
        {
            query = query.OrderByDescending(specification.OrderByDescending);
        }

        if (specification.GroupBy is not null)
        {
            query = query.GroupBy(specification.GroupBy).SelectMany(group => group);
        }

        if (specification.IsPagingEnabled)
        {
            query = query.Skip(specification.Skip).Take(specification.Take);
        }

        return query;
    }
}
