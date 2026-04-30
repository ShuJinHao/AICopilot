using AICopilot.IdentityService.Authorization;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;

namespace AICopilot.IdentityService.Queries;

public record RoleSummaryDto(
    string RoleId,
    string RoleName,
    IReadOnlyCollection<string> Permissions,
    bool IsSystemRole,
    int AssignedUserCount);

[AuthorizeRequirement("Identity.GetListRoles")]
public record GetListRolesQuery : IQuery<Result<IReadOnlyCollection<RoleSummaryDto>>>;

public sealed class GetListRolesQueryHandler(
    RoleManager<IdentityRole<Guid>> roleManager,
    UserManager<ApplicationUser> userManager,
    IIdentityAccessService identityAccessService)
    : IQueryHandler<GetListRolesQuery, Result<IReadOnlyCollection<RoleSummaryDto>>>
{
    public async Task<Result<IReadOnlyCollection<RoleSummaryDto>>> Handle(
        GetListRolesQuery request,
        CancellationToken cancellationToken)
    {
        var roles = roleManager.Roles
            .OrderBy(role => role.Name)
            .ToArray();

        var results = new List<RoleSummaryDto>(roles.Length);
        foreach (var role in roles)
        {
            var permissions = await identityAccessService.GetPermissionsAsync(role.Name!, cancellationToken);
            var assignedUserCount = (await userManager.GetUsersInRoleAsync(role.Name!)).Count;

            results.Add(new RoleSummaryDto(
                role.Id.ToString(),
                role.Name!,
                permissions,
                string.Equals(role.Name, IdentityRoleNames.Admin, StringComparison.Ordinal)
                    || string.Equals(role.Name, IdentityRoleNames.User, StringComparison.Ordinal),
                assignedUserCount));
        }

        return Result.Success<IReadOnlyCollection<RoleSummaryDto>>(results);
    }
}
