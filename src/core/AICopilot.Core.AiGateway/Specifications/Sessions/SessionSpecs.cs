using AICopilot.SharedKernel.Specification;
using AICopilot.Core.AiGateway.Ids;

namespace AICopilot.Core.AiGateway.Specifications.Sessions;

public sealed class SessionByIdSpec : Specification<Aggregates.Sessions.Session>
{
    public SessionByIdSpec(SessionId id)
    {
        FilterCondition = session => session.Id == id;
    }
}

public sealed class SessionByIdForUserSpec : Specification<Aggregates.Sessions.Session>
{
    public SessionByIdForUserSpec(SessionId id, Guid userId)
    {
        FilterCondition = session => session.Id == id && session.UserId == userId;
    }
}

public sealed class SessionWithMessagesByIdSpec : Specification<Aggregates.Sessions.Session>
{
    public SessionWithMessagesByIdSpec(SessionId id)
    {
        FilterCondition = session => session.Id == id;
        AddInclude(session => session.Messages);
    }
}

public sealed class SessionWithMessagesByIdForUserSpec : Specification<Aggregates.Sessions.Session>
{
    public SessionWithMessagesByIdForUserSpec(SessionId id, Guid userId)
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
