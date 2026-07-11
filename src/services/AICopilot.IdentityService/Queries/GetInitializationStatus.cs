using AICopilot.Services.Contracts;
using AICopilot.IdentityService.Authorization;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace AICopilot.IdentityService.Queries;

internal static class BootstrapAdminConfiguration
{
    public const string SectionName = "BootstrapAdmin";
}

public record InitializationStatusDto(
    bool HasAdminRole,
    bool HasUserRole,
    bool BootstrapAdminConfigured,
    bool HasEnabledAdminUser,
    bool IsInitialized);

public record GetInitializationStatusQuery : IQuery<Result<InitializationStatusDto>>;

public class GetInitializationStatusQueryHandler(
    RoleManager<IdentityRole<Guid>> roleManager,
    EnabledAdminInvariantPolicy enabledAdminInvariant,
    IConfiguration configuration)
    : IQueryHandler<GetInitializationStatusQuery, Result<InitializationStatusDto>>
{
    public async Task<Result<InitializationStatusDto>> Handle(
        GetInitializationStatusQuery request,
        CancellationToken cancellationToken)
    {
        var hasAdminRole = await roleManager.RoleExistsAsync("Admin");
        var hasUserRole = await roleManager.RoleExistsAsync("User");

        var bootstrapAdminSection = configuration.GetSection(BootstrapAdminConfiguration.SectionName);
        var bootstrapAdminConfigured =
            !string.IsNullOrWhiteSpace(bootstrapAdminSection["UserName"]) &&
            !string.IsNullOrWhiteSpace(bootstrapAdminSection["Password"]);

        var hasEnabledAdminUser = hasAdminRole &&
                                  await enabledAdminInvariant.HasEnabledAdminAsync();

        var result = new InitializationStatusDto(
            hasAdminRole,
            hasUserRole,
            bootstrapAdminConfigured,
            hasEnabledAdminUser,
            hasAdminRole && hasUserRole && hasEnabledAdminUser);

        return Result.Success(result);
    }
}
