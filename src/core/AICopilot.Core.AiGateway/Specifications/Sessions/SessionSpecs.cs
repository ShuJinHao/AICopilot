using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.Sessions;

public sealed class SessionByIdSpec : Specification<Aggregates.Sessions.Session>
{
    public SessionByIdSpec(Guid id)
    {
        FilterCondition = session => session.Id == id;
    }
}

public sealed class SessionByIdForUserSpec : Specification<Aggregates.Sessions.Session>
{
    public SessionByIdForUserSpec(Guid id, Guid userId)
    {
        FilterCondition = session => session.Id == id && session.UserId == userId;
    }
}

public sealed class SessionWithMessagesByIdSpec : Specification<Aggregates.Sessions.Session>
{
    public SessionWithMessagesByIdSpec(Guid id)
    {
        FilterCondition = session => session.Id == id;
        AddInclude(session => session.Messages);
    }
}

public sealed class SessionWithMessagesByIdForUserSpec : Specification<Aggregates.Sessions.Session>
{
    public SessionWithMessagesByIdForUserSpec(Guid id, Guid userId)
    {
        FilterCondition = session => session.Id == id && session.UserId == userId;
        AddInclude(session => session.Messages);
    }
}

public sealed class SessionsOrderedSpec : Specification<Aggregates.Sessions.Session>
{
    public SessionsOrderedSpec()
    {
        SetOrderByDescending(session => session.Id);
    }
}

public sealed class SessionsByUserOrderedSpec : Specification<Aggregates.Sessions.Session>
{
    public SessionsByUserOrderedSpec(Guid userId)
    {
        FilterCondition = session => session.UserId == userId;
        SetOrderByDescending(session => session.Id);
    }
}
