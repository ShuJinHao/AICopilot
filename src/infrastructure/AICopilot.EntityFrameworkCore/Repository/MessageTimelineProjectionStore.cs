using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EntityFrameworkCore.Repository;

public sealed class MessageTimelineProjectionStore(AiGatewayDbContext dbContext) : IMessageTimelineProjectionStore
{
    public async Task<List<MessageEvent>> ListBySessionAsync(
        SessionId sessionId,
        bool includeMessage = false,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.MessageEvents.AsQueryable();
        if (includeMessage)
        {
            query = query.Include(messageEvent => messageEvent.Message);
        }

        return await query
            .Where(messageEvent => messageEvent.SessionId == sessionId)
            .OrderBy(messageEvent => messageEvent.Sequence)
            .ToListAsync(cancellationToken);
    }

    public MessageEvent Add(MessageEvent messageEvent)
    {
        dbContext.MessageEvents.Add(messageEvent);
        return messageEvent;
    }
}
