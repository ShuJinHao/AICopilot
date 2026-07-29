using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.EntityFrameworkCore.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AICopilot.EntityFrameworkCore.Transactions;

internal static class MessageTimelineSequenceCoordinator
{
    public static async Task AllocateAsync(
        AiGatewayDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var eventEntries = dbContext.ChangeTracker
            .Entries<MessageEvent>()
            .Where(entry => entry.State == EntityState.Added)
            .ToArray();
        var messageEntries = dbContext.ChangeTracker
            .Entries<Message>()
            .Where(entry => entry.State == EntityState.Added)
            .ToArray();
        if (eventEntries.Length == 0 && messageEntries.Length == 0)
        {
            return;
        }

        var sessionIds = eventEntries
            .Select(entry => entry.Entity.SessionId)
            .Concat(messageEntries.Select(entry => entry.Entity.SessionId))
            .Distinct()
            .OrderBy(sessionId => sessionId.Value)
            .ToArray();
        foreach (var sessionId in sessionIds)
        {
            var isNewSession = dbContext.ChangeTracker
                .Entries<Session>()
                .Any(entry =>
                    entry.State == EntityState.Added &&
                    entry.Entity.Id == sessionId);
            if (!isNewSession)
            {
                var session = await AgentExecutionRowLock.ByIdAsync<Session>(
                    dbContext,
                    sessionId.Value,
                    cancellationToken);
                if (session is null)
                {
                    throw new InvalidOperationException(
                        $"Message timeline session '{sessionId.Value}' no longer exists.");
                }
            }

            var maxEventSequence = isNewSession
                ? 0
                : await dbContext.MessageEvents
                    .AsNoTracking()
                    .Where(messageEvent => messageEvent.SessionId == sessionId)
                    .Select(messageEvent => (int?)messageEvent.Sequence)
                    .MaxAsync(cancellationToken) ?? 0;
            var maxMessageSequence = isNewSession
                ? 0
                : await dbContext.Messages
                    .AsNoTracking()
                    .Where(message => message.SessionId == sessionId)
                    .Select(message => (int?)message.Sequence)
                    .MaxAsync(cancellationToken) ?? 0;
            var nextSequence = Math.Max(maxEventSequence, maxMessageSequence);
            var assignedMessages = new HashSet<Message>(ReferenceEqualityComparer.Instance);

            foreach (var eventEntry in OrderEvents(eventEntries, sessionId))
            {
                var allocatedSequence = checked(++nextSequence);
                eventEntry.Property(messageEvent => messageEvent.Sequence).CurrentValue =
                    allocatedSequence;
                var message = eventEntry.Entity.Message;
                if (message is null || dbContext.Entry(message).State != EntityState.Added)
                {
                    continue;
                }

                dbContext.Entry(message)
                    .Property(candidate => candidate.Sequence)
                    .CurrentValue = allocatedSequence;
                assignedMessages.Add(message);
            }

            foreach (var messageEntry in OrderMessages(messageEntries, sessionId)
                         .Where(entry => !assignedMessages.Contains(entry.Entity)))
            {
                messageEntry.Property(message => message.Sequence).CurrentValue =
                    checked(++nextSequence);
            }
        }
    }

    private static IEnumerable<EntityEntry<MessageEvent>> OrderEvents(
        IEnumerable<EntityEntry<MessageEvent>> entries,
        SessionId sessionId)
    {
        return entries
            .Where(entry => entry.Entity.SessionId == sessionId)
            .OrderBy(entry => entry.Entity.Sequence)
            .ThenBy(entry => entry.Entity.CreatedAt)
            .ThenBy(entry => entry.Entity.Id.Value);
    }

    private static IEnumerable<EntityEntry<Message>> OrderMessages(
        IEnumerable<EntityEntry<Message>> entries,
        SessionId sessionId)
    {
        return entries
            .Where(entry => entry.Entity.SessionId == sessionId)
            .OrderBy(entry => entry.Entity.Sequence)
            .ThenBy(entry => entry.Entity.CreatedAt);
    }
}
