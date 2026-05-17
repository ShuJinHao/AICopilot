using AICopilot.Services.Contracts;

namespace AICopilot.BackendTests;

internal sealed class TestCurrentUser(
    Guid? id = null,
    string role = "User",
    string userName = "test-user") : ICurrentUser
{
    public Guid? Id { get; } = id ?? Guid.Parse("11111111-1111-4111-8111-111111111111");

    public string? UserName { get; } = userName;

    public string? Role { get; } = role;

    public string? IdentityProvider => "Test";

    public string? CloudTenantId => null;

    public string? CloudEmployeeNo => null;

    public string? CloudStatusVersion => null;

    public bool IsAuthenticated => Id.HasValue;
}
