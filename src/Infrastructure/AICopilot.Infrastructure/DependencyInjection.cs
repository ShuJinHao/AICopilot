using AICopilot.Infrastructure.Authentication;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text;
using AICopilot.EntityFrameworkCore;

namespace AICopilot.Infrastructure;

public static class DependencyInjection
{
    public static void AddInfrastructures(this IHostApplicationBuilder builder)
    {
        builder.AddEfCore();
        builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
    }
}