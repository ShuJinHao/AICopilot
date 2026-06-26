using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.AiGateway.Specifications.Sessions;

public sealed class MessageEventsBySessionSpec : Specification<MessageEvent>
{
    public MessageEventsBySessionSpec(SessionId sessionId, bool includeMessage = false)
    {
        FilterCondition = item => item.SessionId == sessionId;
        SetOrderBy(item => item.Sequence);
        if (includeMessage)
        {
            AddInclude(item => item.Message!);
        }
    }
}
