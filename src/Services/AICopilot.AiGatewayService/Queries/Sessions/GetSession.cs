using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Specifications.Sessions;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.Sessions;

[AuthorizeRequirement("AiGateway.GetSession")]
public record GetSessionQuery(Guid Id) : IQuery<Result<SessionDto>>;

public class GetSessionQueryHandler(IReadRepository<Session> repository)
    : IQueryHandler<GetSessionQuery, Result<SessionDto>>
{
    public async Task<Result<SessionDto>> Handle(GetSessionQuery request, CancellationToken cancellationToken)
    {
        var result = await repository.FirstOrDefaultAsync(new SessionByIdSpec(request.Id), cancellationToken);
        return result == null ? Result.NotFound() : Result.Success(SessionDtoMapper.Map(result));
    }
}
