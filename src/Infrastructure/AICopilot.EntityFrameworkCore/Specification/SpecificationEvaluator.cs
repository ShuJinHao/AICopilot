using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Specification;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Specification;

public static class SpecificationEvaluator
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

        query = specification.Includes.Aggregate(query, (current, include) => current.Include(include));
        query = specification.IncludeStrings.Aggregate(query, (current, include) => current.Include(include));

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
