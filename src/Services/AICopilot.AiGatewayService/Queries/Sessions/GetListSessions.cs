using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Specifications.Sessions;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.Sessions;

public record SessionDto
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public DateTimeOffset? OnsiteConfirmedAt { get; set; }
    public string? OnsiteConfirmedBy { get; set; }
    public DateTimeOffset? OnsiteConfirmationExpiresAt { get; set; }
}

[AuthorizeRequirement("AiGateway.GetListSessions")]
public record GetListSessionsQuery : IQuery<Result<IList<SessionDto>>>;

public class GetListSessionsQueryHandler(IReadRepository<Session> repository)
    : IQueryHandler<GetListSessionsQuery, Result<IList<SessionDto>>>
{
    public async Task<Result<IList<SessionDto>>> Handle(
        GetListSessionsQuery request,
        CancellationToken cancellationToken)
    {
        var sessions = await repository.ListAsync(new SessionsOrderedSpec(), cancellationToken);
        IList<SessionDto> result = sessions.Select(SessionDtoMapper.Map).ToList();
        return Result.Success(result);
    }
}

internal static class SessionDtoMapper
{
    public static SessionDto Map(Session session)
    {
        return new SessionDto
        {
            Id = session.Id,
            Title = session.Title,
            OnsiteConfirmedAt = session.OnsiteConfirmedAt,
            OnsiteConfirmedBy = session.OnsiteConfirmedBy,
            OnsiteConfirmationExpiresAt = session.OnsiteConfirmationExpiresAt
        };
    }
}
