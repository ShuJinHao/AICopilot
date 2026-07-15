using AICopilot.Services.Contracts;

namespace AICopilot.AgentWorkflowTestKit;

internal sealed class TestCurrentUser(
    Guid? id = null,
    string role = "User",
    string userName = "test-user",
    string? cloudDepartmentId = null,
    string? cloudDepartmentName = null) : ICurrentUser
{
    public Guid? Id { get; } = id ?? Guid.Parse("11111111-1111-4111-8111-111111111111");

    public string? UserName { get; } = userName;

    public string? Role { get; } = role;

    public string? IdentityProvider => "Test";

    public string? CloudTenantId => null;

    public string? CloudEmployeeNo => null;

    public string? CloudDepartmentId { get; } = cloudDepartmentId;

    public string? CloudDepartmentName { get; } = cloudDepartmentName;

    public string? CloudStatusVersion => null;

    public bool IsAuthenticated => Id.HasValue;
}
