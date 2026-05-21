namespace AICopilot.Services.Contracts;

public interface ICurrentUser
{
    Guid? Id { get; }

    string? UserName { get; }

    string? Role { get; }

    string? IdentityProvider { get; }

    string? CloudTenantId { get; }

    string? CloudEmployeeNo { get; }

    string? CloudDepartmentId { get; }

    string? CloudDepartmentName { get; }

    string? CloudStatusVersion { get; }

    bool IsAuthenticated { get; }
}
