using AICopilot.Services.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace AICopilot.MigrationWorkApp;

internal static class MigrationWorkerIdentitySeeder
{
    public static async Task SeedAsync(
        RoleManager<IdentityRole<Guid>> roleManager,
        UserManager<ApplicationUser> userManager,
        IPermissionCatalog permissionCatalog,
        IIdentityAccessService identityAccessService,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        foreach (var role in new[] { "Admin", "User" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid> { Name = role });
            }
        }

        await identityAccessService.SyncRolePermissionsAsync(
            "Admin",
            permissionCatalog.GetDefaultPermissions("Admin"),
            cancellationToken);

        await identityAccessService.SyncRolePermissionsAsync(
            "User",
            permissionCatalog.GetDefaultPermissions("User"),
            cancellationToken);

        var bootstrapAdmin = configuration
            .GetSection(BootstrapAdminOptions.SectionName)
            .Get<BootstrapAdminOptions>();

        if (bootstrapAdmin == null ||
            string.IsNullOrWhiteSpace(bootstrapAdmin.UserName) ||
            string.IsNullOrWhiteSpace(bootstrapAdmin.Password))
        {
            return;
        }

        var adminUser = await userManager.FindByNameAsync(bootstrapAdmin.UserName);
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = bootstrapAdmin.UserName
            };

            var result = await userManager.CreateAsync(adminUser, bootstrapAdmin.Password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "创建 BootstrapAdmin 失败: " + string.Join(",", result.Errors.Select(error => error.Description)));
            }
        }

        var existingRoles = await userManager.GetRolesAsync(adminUser);
        if (existingRoles.Count > 0)
        {
            await userManager.RemoveFromRolesAsync(adminUser, existingRoles);
        }

        if (!await userManager.IsInRoleAsync(adminUser, "Admin"))
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
        }
    }
}
