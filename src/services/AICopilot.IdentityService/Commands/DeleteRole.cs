using AICopilot.IdentityService.Authorization;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;

namespace AICopilot.IdentityService.Commands;

[AuthorizeRequirement("Identity.DeleteRole")]
public record DeleteRoleCommand(string RoleId) : ICommand<Result>;

public sealed class DeleteRoleCommandHandler(
    RoleManager<IdentityRole<Guid>> roleManager,
    UserManager<ApplicationUser> userManager,
    IIdentityAuditLogWriter auditLogWriter,
    ITransactionalExecutionService transactionalExecutionService)
    : ICommandHandler<DeleteRoleCommand, Result>
{
    public async Task<Result> Handle(
        DeleteRoleCommand command,
        CancellationToken cancellationToken)
    {
        return await transactionalExecutionService.ExecuteAsync(async _ =>
        {
            var role = await roleManager.FindByIdAsync(command.RoleId);
            if (role is null)
            {
                return Result.NotFound("角色不存在");
            }

            if (string.Equals(role.Name, IdentityRoleNames.Admin, StringComparison.Ordinal)
                || string.Equals(role.Name, IdentityRoleNames.User, StringComparison.Ordinal))
            {
                await auditLogWriter.WriteAsync(
                    new AuditLogWriteRequest(
                        AuditActionGroups.Identity,
                        "Identity.DeleteRole",
                        "Role",
                        role.Id.ToString(),
                        role.Name ?? string.Empty,
                        AuditResults.Rejected,
                        $"拒绝删除角色：{role.Name}，原因是系统基线角色不可删除。"),
                    cancellationToken);
                return Result.Invalid("系统基线角色不允许删除。");
            }

            var assignedUserCount = (await userManager.GetUsersInRoleAsync(role.Name!)).Count;
            if (assignedUserCount > 0)
            {
                await auditLogWriter.WriteAsync(
                    new AuditLogWriteRequest(
                        AuditActionGroups.Identity,
                        "Identity.DeleteRole",
                        "Role",
                        role.Id.ToString(),
                        role.Name ?? string.Empty,
                        AuditResults.Rejected,
                        $"拒绝删除角色：{role.Name}，原因是仍有 {assignedUserCount} 个绑定用户。"),
                    cancellationToken);
                return Result.Invalid("角色仍有绑定用户，不能删除。");
            }

            var deleteResult = await roleManager.DeleteAsync(role);
            if (!deleteResult.Succeeded)
            {
                return Result.Failure(deleteResult.Errors.ToArray());
            }

            await auditLogWriter.WriteAsync(
                new AuditLogWriteRequest(
                    AuditActionGroups.Identity,
                    "Identity.DeleteRole",
                    "Role",
                    role.Id.ToString(),
                    role.Name ?? string.Empty,
                    AuditResults.Succeeded,
                    $"删除角色：{role.Name}"),
                cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }
}
