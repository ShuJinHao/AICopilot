using AICopilot.Services.Contracts;

namespace AICopilot.HarnessTestKit;

internal sealed class StubIdentityAccessService(
    IReadOnlyCollection<string> permissions,
    string? roleName = "User") : IIdentityAccessService
{
    public Task<CurrentUserAccess?> GetCurrentUserAccessAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<CurrentUserAccess?>(new CurrentUserAccess(
            userId,
            "test-user",
            roleName,
            permissions));

    public Task<IReadOnlyCollection<string>> GetPermissionsAsync(
        string role,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(permissions);

    public Task SyncRolePermissionsAsync(
        string role,
        IEnumerable<string> permissionCodes,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
