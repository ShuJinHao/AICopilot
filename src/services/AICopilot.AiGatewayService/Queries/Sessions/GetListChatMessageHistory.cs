using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Specifications.Sessions;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.Sessions;

public record ChatHistoryMessageDto
{
    public Guid SessionId { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public DateTime CreatedAt { get; init; }
}

[AuthorizeRequirement("AiGateway.GetSession")]
public record GetListChatMessageHistoryQuery(Guid SessionId, int Count = 100, bool IsDesc = false)
    : IQuery<Result<IList<ChatHistoryMessageDto>>>;

public class GetListChatMessageHistoryQueryHandler(
    IReadRepository<Session> repository,
    ICurrentUser currentUser)
    : IQueryHandler<GetListChatMessageHistoryQuery, Result<IList<ChatHistoryMessageDto>>>
{
    public async Task<Result<IList<ChatHistoryMessageDto>>> Handle(
        GetListChatMessageHistoryQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        var count = request.Count <= 0 ? 100 : request.Count;
        var session = await repository.FirstOrDefaultAsync(
            new SessionWithMessagesByIdForUserSpec(request.SessionId, userId),
            cancellationToken);

        if (session is null)
        {
            return Result.NotFound();
        }

        var messages = session.Messages
            .Where(message => message.Type == MessageType.User || message.Type == MessageType.Assistant);

        messages = request.IsDesc
            ? messages.OrderByDescending(message => message.CreatedAt)
            : messages.OrderBy(message => message.CreatedAt);

        IList<ChatHistoryMessageDto> result = messages
            .Take(count)
            .Select(message => new ChatHistoryMessageDto
            {
                SessionId = message.SessionId,
                Role = message.Type.ToString(),
                Content = message.Content,
                CreatedAt = message.CreatedAt
            })
            .ToList();

        return Result.Success(result);
    }
}
