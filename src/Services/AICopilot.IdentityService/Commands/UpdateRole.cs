using AICopilot.IdentityService.Authorization;
using AICopilot.IdentityService.Queries;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;

namespace AICopilot.IdentityService.Commands;

[AuthorizeRequirement("Identity.UpdateRole")]
public record UpdateRoleCommand(string RoleId, IReadOnlyCollection<string> Permissions)
    : ICommand<Result<RoleSummaryDto>>;

public sealed class UpdateRoleCommandHandler(
    RoleManager<IdentityRole<Guid>> roleManager,
    UserManager<ApplicationUser> userManager,
    IIdentityAccessService identityAccessService,
    IPermissionCatalog permissionCatalog,
    IIdentityAuditLogWriter auditLogWriter,
    ITransactionalExecutionService transactionalExecutionService)
    : ICommandHandler<UpdateRoleCommand, Result<RoleSummaryDto>>
{
    public async Task<Result<RoleSummaryDto>> Handle(
        UpdateRoleCommand command,
        CancellationToken cancellationToken)
    {
        return await transactionalExecutionService.ExecuteAsync(async _ =>
        {
            var role = await roleManager.FindByIdAsync(command.RoleId);
            if (role is null)
            {
                return Result.NotFound("角色不存在");
            }

            var invalidPermissions = command.Permissions
                .Where(permission => !permissionCatalog.Exists(permission))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (invalidPermissions.Length > 0)
            {
                return Result.Invalid($"存在未知权限：{string.Join(", ", invalidPermissions)}");
            }

            var currentPermissions = await identityAccessService.GetPermissionsAsync(role.Name!, cancellationToken);

            await identityAccessService.SyncRolePermissionsAsync(role.Name!, command.Permissions, cancellationToken);
            var permissions = await identityAccessService.GetPermissionsAsync(role.Name!, cancellationToken);

            var addedPermissions = permissions.Except(currentPermissions, StringComparer.Ordinal).ToArray();
            var removedPermissions = currentPermissions.Except(permissions, StringComparer.Ordinal).ToArray();

            await auditLogWriter.WriteAsync(
                new AuditLogWriteRequest(
                    AuditActionGroups.Identity,
                    "Identity.UpdateRole",
                    "Role",
                    role.Id.ToString(),
                    role.Name!,
                    AuditResults.Succeeded,
                    $"更新角色权限：{role.Name}，新增 {addedPermissions.Length} 项权限，移除 {removedPermissions.Length} 项权限",
                    ["permissions"]),
                cancellationToken);
            return Result.Success(new RoleSummaryDto(
                role.Id.ToString(),
                role.Name!,
                permissions,
                string.Equals(role.Name, IdentityRoleNames.Admin, StringComparison.Ordinal)
                    || string.Equals(role.Name, IdentityRoleNames.User, StringComparison.Ordinal),
                (await userManager.GetUsersInRoleAsync(role.Name!)).Count));
        }, cancellationToken);
    }
}
