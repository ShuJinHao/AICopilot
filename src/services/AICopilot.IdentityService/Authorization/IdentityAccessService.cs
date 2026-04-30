using AICopilot.Services.Contracts;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace AICopilot.IdentityService.Authorization;

public sealed class IdentityAccessService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IPermissionCatalog permissionCatalog) : IIdentityAccessService
{
    public async Task<CurrentUserAccess?> GetCurrentUserAccessAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return null;
        }

        var roleName = await GetSingleRoleNameAsync(user);
        var permissions = roleName is null
            ? Array.Empty<string>()
            : await GetPermissionsAsync(roleName, cancellationToken);

        return new CurrentUserAccess(user.Id, user.UserName ?? string.Empty, roleName, permissions);
    }

    public async Task<IReadOnlyCollection<string>> GetPermissionsAsync(
        string roleName,
        CancellationToken cancellationToken = default)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is null)
        {
            return Array.Empty<string>();
        }

        var permissionClaims = await roleManager.GetClaimsAsync(role);
        return permissionClaims
            .Where(IsPermissionClaim)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task SyncRolePermissionsAsync(
        string roleName,
        IEnumerable<string> permissionCodes,
        CancellationToken cancellationToken = default)
    {
        var role = await roleManager.FindByNameAsync(roleName)
            ?? throw new InvalidOperationException($"Role '{roleName}' was not found.");

        var normalizedPermissions = permissionCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        var invalidPermissions = normalizedPermissions
            .Where(code => !permissionCatalog.Exists(code))
            .ToArray();

        if (invalidPermissions.Length > 0)
        {
            throw new InvalidOperationException(
                $"Unknown permissions: {string.Join(", ", invalidPermissions)}");
        }

        var existingClaims = await roleManager.GetClaimsAsync(role);
        foreach (var permissionClaim in existingClaims.Where(IsPermissionClaim))
        {
            await roleManager.RemoveClaimAsync(role, permissionClaim);
        }

        foreach (var permissionCode in normalizedPermissions)
        {
            await roleManager.AddClaimAsync(
                role,
                new Claim(IdentityPermissionConstants.PermissionClaimType, permissionCode));
        }
    }

    private static bool IsPermissionClaim(Claim claim)
    {
        return string.Equals(
            claim.Type,
            IdentityPermissionConstants.PermissionClaimType,
            StringComparison.Ordinal);
    }

    private async Task<string?> GetSingleRoleNameAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return roles
            .OrderBy(role => role, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
