using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.IdentityService.Queries;

public record CurrentUserProfileDto(
    string UserId,
    string UserName,
    string? RoleName,
    IReadOnlyCollection<string> Permissions);

public record GetCurrentUserProfileQuery : IQuery<Result<CurrentUserProfileDto>>;

public sealed class GetCurrentUserProfileQueryHandler(
    ICurrentUser currentUser,
    IIdentityAccessService identityAccessService)
    : IQueryHandler<GetCurrentUserProfileQuery, Result<CurrentUserProfileDto>>
{
    public async Task<Result<CurrentUserProfileDto>> Handle(
        GetCurrentUserProfileQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.Id is null)
        {
            return Result.Unauthorized("当前用户未登录");
        }

        var access = await identityAccessService.GetCurrentUserAccessAsync(currentUser.Id.Value, cancellationToken);
        if (access is null)
        {
            return Result.NotFound("当前用户不存在");
        }

        return Result.Success(new CurrentUserProfileDto(
            access.UserId.ToString(),
            access.UserName,
            access.RoleName,
            access.Permissions));
    }
}
