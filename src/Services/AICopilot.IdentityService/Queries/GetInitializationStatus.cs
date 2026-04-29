using AICopilot.Services.Contracts;
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
    bool HasAdminUser,
    bool IsInitialized);

public record GetInitializationStatusQuery : IQuery<Result<InitializationStatusDto>>;

public class GetInitializationStatusQueryHandler(
    RoleManager<IdentityRole<Guid>> roleManager,
    UserManager<ApplicationUser> userManager,
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

        var hasAdminUser = (await userManager.GetUsersInRoleAsync("Admin")).Count > 0;

        var result = new InitializationStatusDto(
            hasAdminRole,
            hasUserRole,
            bootstrapAdminConfigured,
            hasAdminUser,
            hasAdminRole && hasUserRole && (!bootstrapAdminConfigured || hasAdminUser));

        return Result.Success(result);
    }
}
