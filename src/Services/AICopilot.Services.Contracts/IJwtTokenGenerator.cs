using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace AICopilot.Services.Contracts;

public interface IJwtTokenGenerator
{
    Task<string> GenerateTokenAsync(IdentityUser user);
}