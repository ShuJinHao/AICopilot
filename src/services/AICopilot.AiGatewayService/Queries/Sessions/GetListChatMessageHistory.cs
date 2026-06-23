using System.Text.Json;
using AICopilot.AiGatewayService.Models;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Specifications.Sessions;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.Sessions;

public record ChatHistoryMessageDto
{
    public int MessageId { get; init; }
    public int Sequence { get; init; }
    public Guid SessionId { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public DateTime CreatedAt { get; init; }
    public IReadOnlyCollection<ChatChunk> RenderChunks { get; init; } = [];
    public Guid? FinalModelId { get; init; }
    public string? FinalModelName { get; init; }
    public Guid? RoutingModelId { get; init; }
    public string? RoutingModelName { get; init; }
    public int? ContextWindowTokens { get; init; }
    public int? MaxOutputTokens { get; init; }
}

public record ChatHistoryMessagePageDto
{
    public IReadOnlyList<ChatHistoryMessageDto> Items { get; init; } = [];
    public int? BeforeSequence { get; init; }
    public int? AfterSequence { get; init; }
    public bool HasMore { get; init; }
    public bool HasMoreBefore { get; init; }
    public bool HasMoreAfter { get; init; }
}

[AuthorizeRequirement("AiGateway.GetSession")]
public record GetListChatMessageHistoryQuery(
    Guid SessionId,
    int Count = 100,
    bool IsDesc = false,
    int? BeforeSequence = null,
    int? AfterSequence = null)
    : IQuery<Result<ChatHistoryMessagePageDto>>;

public class GetListChatMessageHistoryQueryHandler(
    IReadRepository<Session> repository,
    IReadRepository<MessageEvent> messageEventRepository,
    ICurrentUser currentUser)
    : IQueryHandler<GetListChatMessageHistoryQuery, Result<ChatHistoryMessagePageDto>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<ChatHistoryMessagePageDto>> Handle(
        GetListChatMessageHistoryQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        var count = Math.Clamp(request.Count <= 0 ? 100 : request.Count, 1, 200);
        var session = await repository.FirstOrDefaultAsync(
            new SessionWithMessagesByIdForUserSpec(new SessionId(request.SessionId), userId),
            cancellationToken);

        if (session is null)
        {
            return Result.NotFound();
        }

        var allEvents = await messageEventRepository.ListAsync(
            new MessageEventsBySessionSpec(new SessionId(request.SessionId), includeMessage: true),
            cancellationToken);
        var messageEvents = allEvents
            .Where(item => item.EventType == MessageEventType.Message &&
                           item.Message is { Type: MessageType.User or MessageType.Assistant })
            .OrderBy(item => item.Sequence)
            .ThenBy(item => item.MessageId ?? 0)
            .ToArray();
        if (messageEvents.Length > 0)
        {
            return Result.Success(BuildEventPage(messageEvents, request, count));
        }

        var allMessages = session.Messages
            .Where(message => message.Type == MessageType.User || message.Type == MessageType.Assistant)
            .OrderBy(message => message.Sequence)
            .ThenBy(message => message.Id)
            .ToArray();

        var cursorMessages = allMessages.AsEnumerable();
        if (request.BeforeSequence is > 0)
        {
            cursorMessages = cursorMessages.Where(message => message.Sequence < request.BeforeSequence.Value);
        }
        else if (request.AfterSequence is > 0)
        {
            cursorMessages = cursorMessages.Where(message => message.Sequence > request.AfterSequence.Value);
        }

        var orderedMessages = PageMessages(cursorMessages, request, count);

        var items = orderedMessages
            .Select(message => new ChatHistoryMessageDto
            {
                MessageId = message.Id,
                Sequence = message.Sequence,
                SessionId = message.SessionId,
                Role = message.Type.ToString(),
                Content = message.Content,
                CreatedAt = message.CreatedAt,
                RenderChunks = ResolveRenderChunks(message),
                FinalModelId = message.FinalModelId,
                FinalModelName = message.FinalModelName,
                RoutingModelId = message.RoutingModelId,
                RoutingModelName = message.RoutingModelName,
                ContextWindowTokens = message.ContextWindowTokens,
                MaxOutputTokens = message.MaxOutputTokens
            })
            .ToList();

        var minSequence = items.Count > 0 ? items.Min(message => message.Sequence) : (int?)null;
        var maxSequence = items.Count > 0 ? items.Max(message => message.Sequence) : (int?)null;
        var hasMoreBefore = minSequence.HasValue && allMessages.Any(message => message.Sequence < minSequence.Value);
        var hasMoreAfter = maxSequence.HasValue && allMessages.Any(message => message.Sequence > maxSequence.Value);

        return Result.Success(new ChatHistoryMessagePageDto
        {
            Items = items,
            BeforeSequence = minSequence,
            AfterSequence = maxSequence,
            HasMore = request.AfterSequence is > 0 ? hasMoreAfter : hasMoreBefore,
            HasMoreBefore = hasMoreBefore,
            HasMoreAfter = hasMoreAfter
        });
    }

    private static ChatHistoryMessagePageDto BuildEventPage(
        IReadOnlyCollection<MessageEvent> allEvents,
        GetListChatMessageHistoryQuery request,
        int count)
    {
        var cursorEvents = allEvents.AsEnumerable();
        if (request.BeforeSequence is > 0)
        {
            cursorEvents = cursorEvents.Where(item => item.Sequence < request.BeforeSequence.Value);
        }
        else if (request.AfterSequence is > 0)
        {
            cursorEvents = cursorEvents.Where(item => item.Sequence > request.AfterSequence.Value);
        }

        var orderedEvents = PageEvents(cursorEvents, request, count);
        var items = orderedEvents
            .Select(item => MapEventMessage(item, item.Message!))
            .ToList();
        var minSequence = items.Count > 0 ? items.Min(message => message.Sequence) : (int?)null;
        var maxSequence = items.Count > 0 ? items.Max(message => message.Sequence) : (int?)null;
        var hasMoreBefore = minSequence.HasValue && allEvents.Any(item => item.Sequence < minSequence.Value);
        var hasMoreAfter = maxSequence.HasValue && allEvents.Any(item => item.Sequence > maxSequence.Value);

        return new ChatHistoryMessagePageDto
        {
            Items = items,
            BeforeSequence = minSequence,
            AfterSequence = maxSequence,
            HasMore = request.AfterSequence is > 0 ? hasMoreAfter : hasMoreBefore,
            HasMoreBefore = hasMoreBefore,
            HasMoreAfter = hasMoreAfter
        };
    }

    private static MessageEvent[] PageEvents(
        IEnumerable<MessageEvent> cursorEvents,
        GetListChatMessageHistoryQuery request,
        int count)
    {
        if (request.IsDesc)
        {
            return cursorEvents
                .OrderByDescending(item => item.Sequence)
                .ThenByDescending(item => item.MessageId ?? 0)
                .Take(count)
                .ToArray();
        }

        if (request.AfterSequence is > 0)
        {
            return cursorEvents
                .OrderBy(item => item.Sequence)
                .ThenBy(item => item.MessageId ?? 0)
                .Take(count)
                .ToArray();
        }

        return cursorEvents
            .OrderByDescending(item => item.Sequence)
            .ThenByDescending(item => item.MessageId ?? 0)
            .Take(count)
            .OrderBy(item => item.Sequence)
            .ThenBy(item => item.MessageId ?? 0)
            .ToArray();
    }

    private static ChatHistoryMessageDto MapEventMessage(MessageEvent messageEvent, Message message)
    {
        return new ChatHistoryMessageDto
        {
            MessageId = message.Id,
            Sequence = messageEvent.Sequence,
            SessionId = message.SessionId,
            Role = message.Type.ToString(),
            Content = message.Content,
            CreatedAt = message.CreatedAt,
            RenderChunks = ResolveRenderChunks(message),
            FinalModelId = message.FinalModelId,
            FinalModelName = message.FinalModelName,
            RoutingModelId = message.RoutingModelId,
            RoutingModelName = message.RoutingModelName,
            ContextWindowTokens = message.ContextWindowTokens,
            MaxOutputTokens = message.MaxOutputTokens
        };
    }

    private static Message[] PageMessages(
        IEnumerable<Message> cursorMessages,
        GetListChatMessageHistoryQuery request,
        int count)
    {
        if (request.IsDesc)
        {
            return cursorMessages
                .OrderByDescending(message => message.Sequence)
                .ThenByDescending(message => message.Id)
                .Take(count)
                .ToArray();
        }

        if (request.AfterSequence is > 0)
        {
            return cursorMessages
                .OrderBy(message => message.Sequence)
                .ThenBy(message => message.Id)
                .Take(count)
                .ToArray();
        }

        return cursorMessages
            .OrderByDescending(message => message.Sequence)
            .ThenByDescending(message => message.Id)
            .Take(count)
            .OrderBy(message => message.Sequence)
            .ThenBy(message => message.Id)
            .ToArray();
    }

    private static IReadOnlyCollection<ChatChunk> ResolveRenderChunks(Message message)
    {
        if (!string.IsNullOrWhiteSpace(message.RenderPayloadJson))
        {
            try
            {
                var chunks = JsonSerializer.Deserialize<IReadOnlyCollection<ChatChunk>>(
                    message.RenderPayloadJson,
                    JsonOptions);
                var stableChunks = chunks?
                    .Where(IsStableRenderChunk)
                    .Where(chunk => !string.IsNullOrWhiteSpace(chunk.Content))
                    .ToArray();
                if (stableChunks is { Length: > 0 })
                {
                    return stableChunks;
                }
            }
            catch (JsonException)
            {
                // Fall back to text below. History must remain readable even if a payload is corrupt.
            }
        }

        return
        [
            new ChatChunk(
                message.Type == MessageType.User ? "User" : "FinalAgentRunExecutor",
                ChunkType.Text,
            message.Content)
        ];
    }

    private static bool IsStableRenderChunk(ChatChunk chunk)
    {
        return chunk.Type is ChunkType.Text or ChunkType.Widget or ChunkType.Error;
    }
}
