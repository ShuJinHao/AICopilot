using System.Text.Json;
using AICopilot.AiGatewayService.Models;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;

namespace AICopilot.AiGatewayService.Sessions;

public sealed record SessionMessageAppend(
    string? Content,
    MessageType Type,
    MessageModelSnapshot? ModelSnapshot = null,
    IReadOnlyCollection<ChatChunk>? RenderChunks = null);

public class SessionMessagePersistenceService(
    IRepository<Session> repository,
    IMessageTimelineProjectionStore messageTimelineProjectionStore)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
            .Select(NormalizeEntry)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Content) || entry.RenderChunks?.Count > 0)
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

        var existingEvents = await messageTimelineProjectionStore.ListBySessionAsync(session.Id, cancellationToken: cancellationToken);
        var nextEventSequence = Math.Max(
            existingEvents.Count == 0 ? 0 : existingEvents.Max(item => item.Sequence),
            session.Messages.Count == 0 ? 0 : session.Messages.Max(item => item.Sequence));
        session.EnsureMessageCountAtLeast(nextEventSequence);

        foreach (var entry in normalizedEntries)
        {
            var message = session.AddMessage(
                entry.Content,
                entry.Type,
                entry.ModelSnapshot,
                SerializeRenderChunks(entry));
            messageTimelineProjectionStore.Add(MessageEvent.ForMessage(
                session.Id,
                ++nextEventSequence,
                message));
        }

        repository.Update(session);
        await repository.SaveChangesAsync(cancellationToken);
    }

    private static SessionMessageAppend NormalizeEntry(SessionMessageAppend entry)
    {
        var content = string.IsNullOrWhiteSpace(entry.Content) ? null : entry.Content.Trim();
        var chunks = entry.RenderChunks?
            .Where(chunk => IsStableRenderChunk(chunk) && !string.IsNullOrWhiteSpace(chunk.Content))
            .ToArray();
        if (chunks is { Length: > 0 })
        {
            return entry with { Content = content, RenderChunks = chunks };
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return entry with { Content = null, RenderChunks = [] };
        }

        return entry with
        {
            Content = content,
            RenderChunks =
            [
                new ChatChunk(
                    entry.Type == MessageType.User ? "User" : "FinalAgentRunExecutor",
                    ChunkType.Text,
                    content)
            ]
        };
    }

    private static string? SerializeRenderChunks(SessionMessageAppend entry)
    {
        if (entry.RenderChunks is not { Count: > 0 })
        {
            return null;
        }

        return JsonSerializer.Serialize(entry.RenderChunks, JsonOptions);
    }

    private static bool IsStableRenderChunk(ChatChunk chunk)
    {
        return chunk.Type is ChunkType.Text or ChunkType.Widget or ChunkType.Error;
    }
}
