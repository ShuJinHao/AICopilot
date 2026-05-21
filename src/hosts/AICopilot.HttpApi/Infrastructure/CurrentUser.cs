using AICopilot.Services.Contracts;
using System.Security.Claims;

namespace AICopilot.HttpApi.Infrastructure;

public class CurrentUser : ICurrentUser
{
    public Guid? Id { get; }
    public string? UserName { get; }
    public string? Role { get; }
    public string? IdentityProvider { get; }
    public string? CloudTenantId { get; }
    public string? CloudEmployeeNo { get; }
    public string? CloudDepartmentId { get; }
    public string? CloudDepartmentName { get; }
    public string? CloudStatusVersion { get; }
    public bool IsAuthenticated { get; }

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;

        if (user == null) return;

        if (!user.Identity!.IsAuthenticated) return;

        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdClaim, out var userId))
        {
            Id = userId;
        }

        UserName = user.FindFirstValue(ClaimTypes.Name);
        Role = user.FindFirstValue(ClaimTypes.Role);
        IdentityProvider = user.FindFirstValue(ExternalIdentityJwtClaimTypes.IdentityProvider);
        CloudTenantId = user.FindFirstValue(ExternalIdentityJwtClaimTypes.CloudTenantId);
        CloudEmployeeNo = user.FindFirstValue(ExternalIdentityJwtClaimTypes.CloudEmployeeNo);
        CloudDepartmentId = user.FindFirstValue(ExternalIdentityJwtClaimTypes.CloudDepartmentId);
        CloudDepartmentName = user.FindFirstValue(ExternalIdentityJwtClaimTypes.CloudDepartmentName);
        CloudStatusVersion = user.FindFirstValue(ExternalIdentityJwtClaimTypes.CloudStatusVersion);

        IsAuthenticated = true;
    }
}
