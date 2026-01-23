using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace AICopilot.IdentityService.Contracts;

public interface IJwtTokenGenerator
{
    Task<string> GenerateTokenAsync(IdentityUser user);
}