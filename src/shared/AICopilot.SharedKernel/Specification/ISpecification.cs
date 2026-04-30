using System.Linq.Expressions;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.SharedKernel.Specification;

public interface ISpecification<T> where T : class, IEntity
{
    Expression<Func<T, bool>>? FilterCondition { get; }

    IReadOnlyList<Expression<Func<T, object>>> Includes { get; }

    IReadOnlyList<string> IncludeStrings { get; }

    Expression<Func<T, object>>? OrderBy { get; }

    Expression<Func<T, object>>? OrderByDescending { get; }

    Expression<Func<T, object>>? GroupBy { get; }

    int Take { get; }

    int Skip { get; }

    bool IsPagingEnabled { get; }
}
