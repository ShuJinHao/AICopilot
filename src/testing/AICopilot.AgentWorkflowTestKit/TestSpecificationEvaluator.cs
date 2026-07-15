using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.AgentWorkflowTestKit;

internal static class TestSpecificationEvaluator
{
    public static IQueryable<T> Apply<T>(
        IQueryable<T> query,
        ISpecification<T>? specification)
        where T : class, IEntity
    {
        if (specification is null)
        {
            return query;
        }

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

        return specification.IsPagingEnabled
            ? query.Skip(specification.Skip).Take(specification.Take)
            : query;
    }
}
