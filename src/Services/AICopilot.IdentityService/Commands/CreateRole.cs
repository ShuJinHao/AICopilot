using AICopilot.IdentityService.Authorization;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;

namespace AICopilot.IdentityService.Commands;

public record CreatedRoleDto(
    string RoleId,
    string RoleName,
    IReadOnlyCollection<string> Permissions,
    bool IsSystemRole,
    int AssignedUserCount);

[AuthorizeRequirement("Identity.CreateRole")]
public record CreateRoleCommand(string RoleName, IReadOnlyCollection<string> Permissions)
    : ICommand<Result<CreatedRoleDto>>;

public sealed class CreateRoleCommandHandler(
    RoleManager<IdentityRole<Guid>> roleManager,
    IIdentityAccessService identityAccessService,
    IPermissionCatalog permissionCatalog,
    IIdentityAuditLogWriter auditLogWriter,
    ITransactionalExecutionService transactionalExecutionService)
    : ICommandHandler<CreateRoleCommand, Result<CreatedRoleDto>>
{
    public async Task<Result<CreatedRoleDto>> Handle(
        CreateRoleCommand command,
        CancellationToken cancellationToken)
    {
        return await transactionalExecutionService.ExecuteAsync(async _ =>
        {
            var normalizedRoleName = command.RoleName.Trim();

            if (await roleManager.RoleExistsAsync(normalizedRoleName))
            {
                return Result.Invalid("角色已存在");
            }

            var invalidPermissions = command.Permissions
                .Where(permission => !permissionCatalog.Exists(permission))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (invalidPermissions.Length > 0)
            {
                return Result.Invalid($"存在未知权限：{string.Join(", ", invalidPermissions)}");
            }

            var role = new IdentityRole<Guid>
            {
                Name = normalizedRoleName
            };

            var result = await roleManager.CreateAsync(role);
            if (!result.Succeeded)
            {
                return Result.Failure(result.Errors);
            }

            await identityAccessService.SyncRolePermissionsAsync(role.Name!, command.Permissions, cancellationToken);

            await auditLogWriter.WriteAsync(
                new AuditLogWriteRequest(
                    AuditActionGroups.Identity,
                    "Identity.CreateRole",
                    "Role",
                    role.Id.ToString(),
                    role.Name!,
                    AuditResults.Succeeded,
                    $"创建角色：{role.Name}，已分配 {command.Permissions.Count} 项权限",
                    ["permissions"]),
                cancellationToken);
            var permissions = await identityAccessService.GetPermissionsAsync(role.Name!, cancellationToken);
            return Result.Success(new CreatedRoleDto(
                role.Id.ToString(),
                role.Name!,
                permissions,
                string.Equals(role.Name, IdentityRoleNames.Admin, StringComparison.Ordinal)
                    || string.Equals(role.Name, IdentityRoleNames.User, StringComparison.Ordinal),
                0));
        }, cancellationToken);
    }
}
