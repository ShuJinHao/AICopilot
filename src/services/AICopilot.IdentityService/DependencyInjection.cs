using AICopilot.IdentityService.Authorization;
using AICopilot.IdentityService.Services;
using AICopilot.Services.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Reflection;

namespace AICopilot.IdentityService;

public static class DependencyInjection
{
    public static void AddIdentityService(this IHostApplicationBuilder builder)
    {
        builder.Services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
        });

        builder.Services.AddScoped<IPermissionCatalog, PermissionCatalog>();
        builder.Services.AddScoped<IIdentityAccessService, IdentityAccessService>();
        builder.Services.AddScoped<EnabledAdminInvariantPolicy>();
        builder.Services.AddSingleton<ICloudIdentityStatusValidationCache, CloudIdentityStatusValidationCache>();
        builder.Services.AddScoped<ICloudIdentityStatusValidator, CloudIdentityStatusValidator>();
    }
}
