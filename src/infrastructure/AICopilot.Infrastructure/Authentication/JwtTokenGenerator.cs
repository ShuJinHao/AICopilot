using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AICopilot.Infrastructure.Authentication;

public class JwtTokenGenerator(IOptions<JwtSettings> jwtSettings) : IJwtTokenGenerator
{
    public Task<string> GenerateTokenAsync(
        JwtTokenUser user,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var issuer = jwtSettings.Value.Issuer;
        var audience = jwtSettings.Value.Audience;
        var secretKey = jwtSettings.Value.SecretKey;
        var accessTokenExpirationMinutes = jwtSettings.Value.AccessTokenExpirationMinutes;

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

        var authClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtClaimTypes.SecurityStamp, user.SecurityStamp),
        };

        authClaims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        authClaims.AddRange(user.Claims);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(authClaims),
            Issuer = issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(accessTokenExpirationMinutes),
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);

        return Task.FromResult(tokenHandler.WriteToken(token));
    }
}
