using AICopilot.IdentityService.Authorization;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;

namespace AICopilot.IdentityService.Queries;

public record UserSummaryDto(
    string UserId,
    string UserName,
    string? RoleName,
    bool IsEnabled,
    string Status);

[AuthorizeRequirement("Identity.GetListUsers")]
public record GetListUsersQuery : IQuery<Result<IReadOnlyCollection<UserSummaryDto>>>;

public sealed class GetListUsersQueryHandler(
    UserManager<ApplicationUser> userManager)
    : IQueryHandler<GetListUsersQuery, Result<IReadOnlyCollection<UserSummaryDto>>>
{
    public async Task<Result<IReadOnlyCollection<UserSummaryDto>>> Handle(
        GetListUsersQuery request,
        CancellationToken cancellationToken)
    {
        var users = userManager.Users
            .OrderBy(user => user.UserName)
            .ToList();

        var results = new List<UserSummaryDto>(users.Count);
        foreach (var user in users)
        {
            var roleName = (await userManager.GetRolesAsync(user))
                .OrderBy(role => role, StringComparer.Ordinal)
                .FirstOrDefault();

            var isEnabled = !IdentityGovernanceHelper.IsUserDisabled(user);

            results.Add(new UserSummaryDto(
                user.Id.ToString(),
                user.UserName ?? string.Empty,
                roleName,
                isEnabled,
                IdentityGovernanceHelper.GetUserStatus(user)));
        }

        return Result.Success<IReadOnlyCollection<UserSummaryDto>>(results);
    }
}
