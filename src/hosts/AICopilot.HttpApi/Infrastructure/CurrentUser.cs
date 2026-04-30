using AICopilot.Services.Contracts;
using System.Security.Claims;

namespace AICopilot.HttpApi.Infrastructure;

public class CurrentUser : ICurrentUser
{
    public Guid? Id { get; }
    public string? UserName { get; }
    public string? Role { get; }
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

        IsAuthenticated = true;
    }
}
