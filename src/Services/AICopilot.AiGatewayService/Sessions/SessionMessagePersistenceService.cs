using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.Sessions;

public sealed record SessionMessageAppend(string? Content, MessageType Type);

public class SessionMessagePersistenceService(IRepository<Session> repository)
{
    public async Task AppendAsync(
        Guid sessionId,
        string? content,
        MessageType type,
        CancellationToken cancellationToken = default)
    {
        await AppendBatchAsync(sessionId, [new SessionMessageAppend(content, type)], cancellationToken);
    }

    public async Task AppendBatchAsync(
        Guid sessionId,
        IEnumerable<SessionMessageAppend> entries,
        CancellationToken cancellationToken = default)
    {
        var normalizedEntries = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Content))
            .Select(entry => new SessionMessageAppend(entry.Content!.Trim(), entry.Type))
            .ToArray();
        if (normalizedEntries.Length == 0)
        {
            return;
        }

        var session = await repository.GetAsync(
            entity => entity.Id == sessionId,
            [entity => entity.Messages],
            cancellationToken);

        if (session == null)
        {
            return;
        }

        foreach (var entry in normalizedEntries)
        {
            session.AddMessage(entry.Content!, entry.Type);
        }

        repository.Update(session);
        await repository.SaveChangesAsync(cancellationToken);
    }
}
