using AICopilot.Services.Contracts;
using Microsoft.AspNetCore.Identity;

namespace AICopilot.IdentityService.Authorization;

public sealed class EnabledAdminInvariantPolicy(
    UserManager<ApplicationUser> userManager,
    IIdentityEnabledAdminInvariantGuard invariantGuard)
{
    public Task AcquireAsync(CancellationToken cancellationToken = default)
    {
        return invariantGuard.AcquireAsync(cancellationToken);
    }

    public async Task<bool> IsLastEnabledAdminAsync(
        ApplicationUser user,
        IEnumerable<string> currentRoles)
    {
        if (IdentityGovernanceHelper.IsUserDisabled(user) ||
            !currentRoles.Contains(IdentityRoleNames.Admin, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var users = await userManager.GetUsersInRoleAsync(IdentityRoleNames.Admin);
        return users.Count(candidate => !IdentityGovernanceHelper.IsUserDisabled(candidate)) <= 1;
    }

    public async Task<bool> HasEnabledAdminAsync()
    {
        var users = await userManager.GetUsersInRoleAsync(IdentityRoleNames.Admin);
        return users.Any(user => !IdentityGovernanceHelper.IsUserDisabled(user));
    }
}
