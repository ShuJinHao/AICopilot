using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Specifications.Sessions;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Queries.Sessions;

[AuthorizeRequirement("AiGateway.GetSession")]
public record GetSessionQuery(Guid Id) : IQuery<Result<SessionDto>>;

public class GetSessionQueryHandler(
    IReadRepository<Session> repository,
    ICurrentUser currentUser)
    : IQueryHandler<GetSessionQuery, Result<SessionDto>>
{
    public async Task<Result<SessionDto>> Handle(GetSessionQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        var result = await repository.FirstOrDefaultAsync(new SessionByIdForUserSpec(request.Id, userId), cancellationToken);
        return result == null ? Result.NotFound() : Result.Success(SessionDtoMapper.Map(result));
    }
}
