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

        var allMessages = session.Messages
            .Where(message => message.Type is MessageType.User or MessageType.Assistant)
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

        var page = PageMessages(cursorMessages, request, count);
        var items = page.Select(Map).ToList();
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

    private static Message[] PageMessages(
        IEnumerable<Message> messages,
        GetListChatMessageHistoryQuery request,
        int count)
    {
        if (request.IsDesc)
        {
            return messages
                .OrderByDescending(message => message.Sequence)
                .ThenByDescending(message => message.Id)
                .Take(count)
                .ToArray();
        }

        if (request.AfterSequence is > 0)
        {
            return messages
                .OrderBy(message => message.Sequence)
                .ThenBy(message => message.Id)
                .Take(count)
                .ToArray();
        }

        return messages
            .OrderByDescending(message => message.Sequence)
            .ThenByDescending(message => message.Id)
            .Take(count)
            .OrderBy(message => message.Sequence)
            .ThenBy(message => message.Id)
            .ToArray();
    }

    private static ChatHistoryMessageDto Map(Message message) => new()
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
        ContextWindowTokens = message.ContextWindowTokens,
        MaxOutputTokens = message.MaxOutputTokens
    };

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
                // A corrupt optional render payload cannot make message history unreadable.
            }
        }

        return
        [
            new ChatChunk(
                message.Type == MessageType.User ? "User" : "HarnessAgent",
                ChunkType.Text,
                message.Content)
        ];
    }

    private static bool IsStableRenderChunk(ChatChunk chunk) =>
        chunk.Type is ChunkType.Text or ChunkType.Widget or ChunkType.Error;
}
