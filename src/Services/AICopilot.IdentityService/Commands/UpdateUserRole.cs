using AICopilot.IdentityService.Authorization;
using AICopilot.IdentityService.Queries;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;

namespace AICopilot.IdentityService.Commands;

[AuthorizeRequirement("Identity.UpdateUserRole")]
public record UpdateUserRoleCommand(string UserId, string RoleName)
    : ICommand<Result<UserSummaryDto>>;

public sealed class UpdateUserRoleCommandHandler(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IIdentityAuditLogWriter auditLogWriter,
    ITransactionalExecutionService transactionalExecutionService)
    : ICommandHandler<UpdateUserRoleCommand, Result<UserSummaryDto>>
{
    public async Task<Result<UserSummaryDto>> Handle(
        UpdateUserRoleCommand command,
        CancellationToken cancellationToken)
    {
        return await transactionalExecutionService.ExecuteAsync(async _ =>
        {
            var user = await userManager.FindByIdAsync(command.UserId);
            if (user is null)
            {
                return Result.NotFound("用户不存在");
            }

            var normalizedRoleName = command.RoleName.Trim();
            if (!await roleManager.RoleExistsAsync(normalizedRoleName))
            {
                return Result.NotFound("指定角色不存在");
            }

            var existingRoles = await userManager.GetRolesAsync(user);
            var previousRoleName = existingRoles
                .OrderBy(role => role, StringComparer.Ordinal)
                .FirstOrDefault();

            if (existingRoles.Count > 0)
            {
                await userManager.RemoveFromRolesAsync(user, existingRoles);
            }

            var addToRoleResult = await userManager.AddToRoleAsync(user, normalizedRoleName);
            if (!addToRoleResult.Succeeded)
            {
                return Result.Failure(addToRoleResult.Errors);
            }

            var changedFields = previousRoleName == normalizedRoleName
                ? Array.Empty<string>()
                : ["roleName"];

            await auditLogWriter.WriteAsync(
                new AuditLogWriteRequest(
                    AuditActionGroups.Identity,
                    "Identity.UpdateUserRole",
                    "User",
                    user.Id.ToString(),
                    user.UserName ?? string.Empty,
                    AuditResults.Succeeded,
                    previousRoleName == null
                        ? $"调整用户角色：{user.UserName}，当前角色为 {normalizedRoleName}"
                        : $"调整用户角色：{user.UserName}，由 {previousRoleName} 变更为 {normalizedRoleName}",
                    changedFields),
                cancellationToken);
            return Result.Success(new UserSummaryDto(
                user.Id.ToString(),
                user.UserName ?? string.Empty,
                normalizedRoleName,
                !IdentityGovernanceHelper.IsUserDisabled(user),
                IdentityGovernanceHelper.GetUserStatus(user)));
        }, cancellationToken);
    }
}
