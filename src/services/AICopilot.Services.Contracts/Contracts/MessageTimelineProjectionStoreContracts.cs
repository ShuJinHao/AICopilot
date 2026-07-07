using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;

namespace AICopilot.Services.Contracts;

public interface IMessageTimelineProjectionStore
{
    Task<List<MessageEvent>> ListBySessionAsync(
        SessionId sessionId,
        bool includeMessage = false,
        CancellationToken cancellationToken = default);

    MessageEvent Add(MessageEvent messageEvent);
}
