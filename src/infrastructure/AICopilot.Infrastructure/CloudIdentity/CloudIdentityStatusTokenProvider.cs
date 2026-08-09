using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AICopilot.Infrastructure.CloudIdentity;

public sealed class CloudIdentityStatusTokenProvider(
    IOptions<CloudIdentityStatusOptions> options,
    TimeProvider timeProvider) : ICloudIdentityStatusTokenProvider
{
    private static readonly TimeSpan Lifetime =
        TimeSpan.FromMinutes(CloudIdentityStatusTokenDefaults.LifetimeMinutes);
    private static readonly TimeSpan RenewBefore =
        TimeSpan.FromSeconds(CloudIdentityStatusTokenDefaults.RenewBeforeSeconds);
    private readonly object gate = new();
    private CachedToken? cachedToken;

    public ValueTask<string> GetTokenAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configuredOptions = options.Value;
        configuredOptions.EnsureValid();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        lock (gate)
        {
            if (cachedToken is not null && cachedToken.ExpiresAtUtc - now > RenewBefore)
            {
                return ValueTask.FromResult(cachedToken.Value);
            }

            cachedToken = CreateToken(configuredOptions.SigningSecret, now);
            return ValueTask.FromResult(cachedToken.Value);
        }
    }

    public void Invalidate()
    {
        lock (gate)
        {
            cachedToken = null;
        }
    }

    private static CachedToken CreateToken(string signingSecret, DateTime nowUtc)
    {
        var expiresAtUtc = nowUtc.Add(Lifetime);
        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(signingSecret));
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, CloudIdentityStatusTokenDefaults.Subject),
            new Claim(ClaimTypes.NameIdentifier, CloudIdentityStatusTokenDefaults.Subject),
            new Claim(ClaimTypes.Name, CloudIdentityStatusTokenDefaults.Subject),
            new Claim(
                CloudIdentityStatusTokenDefaults.ActorClaimType,
                CloudIdentityStatusTokenDefaults.Actor),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D"))
        };
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = CloudIdentityStatusTokenDefaults.Issuer,
            Audience = CloudIdentityStatusTokenDefaults.Audience,
            IssuedAt = nowUtc,
            NotBefore = nowUtc,
            Expires = expiresAtUtc,
            SigningCredentials = new SigningCredentials(
                signingKey,
                SecurityAlgorithms.HmacSha256)
        };
        var handler = new JwtSecurityTokenHandler();
        return new CachedToken(
            handler.WriteToken(handler.CreateToken(descriptor)),
            expiresAtUtc);
    }

    private sealed record CachedToken(string Value, DateTime ExpiresAtUtc);
}
