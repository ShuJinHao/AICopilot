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
        ITransactionalExecutionService transactionalExecutionService,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await transactionalExecutionService.ExecuteAsync(async ct =>
        {
            foreach (var role in new[] { "Admin", "User" })
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    EnsureSucceeded(
                        await roleManager.CreateAsync(new IdentityRole<Guid> { Name = role }),
                        $"create bootstrap role '{role}'");
                }
            }

            await identityAccessService.SyncRolePermissionsAsync(
                "Admin",
                permissionCatalog.GetDefaultPermissions("Admin"),
                ct);

            await identityAccessService.SyncRolePermissionsAsync(
                "User",
                permissionCatalog.GetDefaultPermissions("User"),
                ct);

            var bootstrapAdmin = configuration
                .GetSection(BootstrapAdminOptions.SectionName)
                .Get<BootstrapAdminOptions>();

            if (bootstrapAdmin == null ||
                string.IsNullOrWhiteSpace(bootstrapAdmin.UserName) ||
                string.IsNullOrWhiteSpace(bootstrapAdmin.Password))
            {
                return true;
            }

            var adminUser = await userManager.FindByNameAsync(bootstrapAdmin.UserName);
            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = bootstrapAdmin.UserName
                };

                EnsureSucceeded(
                    await userManager.CreateAsync(adminUser, bootstrapAdmin.Password),
                    "create BootstrapAdmin");
            }

            var existingRoles = await userManager.GetRolesAsync(adminUser);
            var rolesToRemove = existingRoles
                .Where(role => !string.Equals(role, "Admin", StringComparison.Ordinal))
                .ToArray();
            if (rolesToRemove.Length > 0)
            {
                EnsureSucceeded(
                    await userManager.RemoveFromRolesAsync(adminUser, rolesToRemove),
                    "remove non-admin roles from BootstrapAdmin");
            }

            if (!existingRoles.Contains("Admin", StringComparer.Ordinal))
            {
                EnsureSucceeded(
                    await userManager.AddToRoleAsync(adminUser, "Admin"),
                    "assign BootstrapAdmin to Admin role");
            }

            return true;
        }, cancellationToken);
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Unable to {operation}: " +
                string.Join(", ", result.Errors.Select(error => error.Description)));
        }
    }
}
