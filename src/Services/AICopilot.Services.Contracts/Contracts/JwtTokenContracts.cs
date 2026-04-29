using System.Security.Claims;

namespace AICopilot.Services.Contracts;

public sealed record JwtTokenUser(
    Guid Id,
    string UserName,
    string SecurityStamp,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<Claim> Claims);

public interface IJwtTokenGenerator
{
    Task<string> GenerateTokenAsync(JwtTokenUser user, CancellationToken cancellationToken = default);
}
