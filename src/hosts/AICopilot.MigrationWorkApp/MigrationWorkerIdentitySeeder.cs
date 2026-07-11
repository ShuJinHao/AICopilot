using AICopilot.Services.Contracts;
using AICopilot.IdentityService.Authorization;
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
        EnabledAdminInvariantPolicy enabledAdminInvariant,
        ITransactionalExecutionService transactionalExecutionService,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await transactionalExecutionService.ExecuteAsync(async ct =>
        {
            await enabledAdminInvariant.AcquireAsync(ct);

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

            if (bootstrapAdmin is not null &&
                !string.IsNullOrWhiteSpace(bootstrapAdmin.UserName) &&
                !string.IsNullOrWhiteSpace(bootstrapAdmin.Password))
            {
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
                    .Where(role => !string.Equals(
                        role,
                        IdentityRoleNames.Admin,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (rolesToRemove.Length > 0)
                {
                    EnsureSucceeded(
                        await userManager.RemoveFromRolesAsync(adminUser, rolesToRemove),
                        "remove non-admin roles from BootstrapAdmin");
                }

                if (!existingRoles.Contains(
                        IdentityRoleNames.Admin,
                        StringComparer.OrdinalIgnoreCase))
                {
                    EnsureSucceeded(
                        await userManager.AddToRoleAsync(adminUser, IdentityRoleNames.Admin),
                        "assign BootstrapAdmin to Admin role");
                }
            }

            if (!await enabledAdminInvariant.HasEnabledAdminAsync())
            {
                throw new InvalidOperationException(
                    "Identity initialization requires at least one enabled Admin. " +
                    "Configure a new BootstrapAdmin or explicitly recover an existing Admin before retrying migration.");
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
