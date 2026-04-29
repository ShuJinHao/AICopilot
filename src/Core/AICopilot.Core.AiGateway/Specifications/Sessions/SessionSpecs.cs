using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.Sessions;

public sealed class SessionByIdSpec : Specification<Aggregates.Sessions.Session>
{
    public SessionByIdSpec(Guid id)
    {
        FilterCondition = session => session.Id == id;
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

public sealed class SessionsOrderedSpec : Specification<Aggregates.Sessions.Session>
{
    public SessionsOrderedSpec()
    {
        SetOrderByDescending(session => session.Id);
    }
}
