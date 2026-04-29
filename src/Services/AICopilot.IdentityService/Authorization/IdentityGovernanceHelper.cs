using AICopilot.Services.Contracts;
using Microsoft.AspNetCore.Identity;

namespace AICopilot.IdentityService.Authorization;

public static class IdentityRoleNames
{
    public const string Admin = "Admin";
    public const string User = "User";
}

public static class UserGovernanceStatuses
{
    public const string Enabled = "Enabled";
    public const string Disabled = "Disabled";
}

public static class IdentityGovernanceHelper
{
    private static readonly DateTimeOffset DisabledUntil = DateTimeOffset.MaxValue;

    public static bool IsUserDisabled(ApplicationUser user)
    {
        return user.LockoutEnabled
            && user.LockoutEnd.HasValue
            && user.LockoutEnd.Value > DateTimeOffset.UtcNow;
    }

    public static string GetUserStatus(ApplicationUser user)
    {
        return IsUserDisabled(user)
            ? UserGovernanceStatuses.Disabled
            : UserGovernanceStatuses.Enabled;
    }

    public static void MarkUserDisabled(ApplicationUser user)
    {
        user.LockoutEnabled = true;
        user.LockoutEnd = DisabledUntil;
    }

    public static void MarkUserEnabled(ApplicationUser user)
    {
        user.LockoutEnabled = false;
        user.LockoutEnd = null;
    }

    public static void RefreshSecurityStamp(ApplicationUser user)
    {
        user.SecurityStamp = Guid.NewGuid().ToString("N");
    }
}
