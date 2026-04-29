using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Specifications.Sessions;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.Sessions;

[AuthorizeRequirement("AiGateway.GetSession")]
public record GetListChatMessagesQuery(Guid SessionId, int Count, bool IsDesc = true) : IQuery<Result<List<AiChatMessage>>>;

public class GetListChatMessagesQueryHandler(IReadRepository<Session> repository)
    : IQueryHandler<GetListChatMessagesQuery, Result<List<AiChatMessage>>>
{
    public async Task<Result<List<AiChatMessage>>> Handle(
        GetListChatMessagesQuery request,
        CancellationToken cancellationToken)
    {
        var session = await repository.FirstOrDefaultAsync(
            new SessionWithMessagesByIdSpec(request.SessionId),
            cancellationToken);

        var messages = session?.Messages ?? [];
        var ordered = request.IsDesc
            ? messages.OrderByDescending(message => message.CreatedAt)
            : messages.OrderBy(message => message.CreatedAt);

        var chatMessages = ordered
            .Take(request.Count)
            .OrderBy(message => message.CreatedAt)
            .Select(message => new AiChatMessage(MapRole(message.Type), message.Content))
            .ToList();

        return Result.Success(chatMessages);
    }

    private static AiChatRole MapRole(MessageType type)
    {
        return type switch
        {
            MessageType.User => AiChatRole.User,
            MessageType.Assistant => AiChatRole.Assistant,
            MessageType.System => AiChatRole.System,
            _ => AiChatRole.User
        };
    }
}
