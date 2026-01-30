using AICopilot.EntityFrameworkCore;
using AICopilot.IdentityService.Contracts;
using AICopilot.Infrastructure.Authentication;
using AICopilot.Infrastructure.Storage;
using AICopilot.Services.Common.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text;

namespace AICopilot.Infrastructure;

public static class DependencyInjection
{
    public static void AddInfrastructures(this IHostApplicationBuilder builder)
    {
        builder.AddEfCore();
        builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();
        builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
    }
}