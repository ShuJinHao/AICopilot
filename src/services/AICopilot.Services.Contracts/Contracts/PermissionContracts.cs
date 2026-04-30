namespace AICopilot.Services.Contracts;

public sealed record PermissionDefinition(
    string Code,
    string Group,
    string DisplayName,
    string Description);

public sealed record CurrentUserAccess(
    Guid UserId,
    string UserName,
    string? RoleName,
    IReadOnlyCollection<string> Permissions);

public interface IPermissionCatalog
{
    IReadOnlyCollection<PermissionDefinition> GetAll();

    IReadOnlyCollection<string> GetDefaultPermissions(string roleName);

    bool Exists(string permissionCode);
}

public interface IIdentityAccessService
{
    Task<CurrentUserAccess?> GetCurrentUserAccessAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>> GetPermissionsAsync(
        string roleName,
        CancellationToken cancellationToken = default);

    Task SyncRolePermissionsAsync(
        string roleName,
        IEnumerable<string> permissionCodes,
        CancellationToken cancellationToken = default);
}
