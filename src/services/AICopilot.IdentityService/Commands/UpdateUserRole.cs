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
    EnabledAdminInvariantPolicy enabledAdminInvariant,
    IIdentityAuditLogWriter auditLogWriter,
    ITransactionalExecutionService transactionalExecutionService)
    : ICommandHandler<UpdateUserRoleCommand, Result<UserSummaryDto>>
{
    public async Task<Result<UserSummaryDto>> Handle(
        UpdateUserRoleCommand command,
        CancellationToken cancellationToken)
    {
        AuditLogWriteRequest? rejectionAudit = null;
        var result = await transactionalExecutionService.ExecuteResultAsync(async _ =>
        {
            await enabledAdminInvariant.AcquireAsync(cancellationToken);

            var user = await userManager.FindByIdAsync(command.UserId);
            if (user is null)
            {
                return Result.NotFound("用户不存在");
            }

            var normalizedRoleName = command.RoleName.Trim();
            var targetRole = await roleManager.FindByNameAsync(normalizedRoleName);
            if (targetRole?.Name is not { } targetRoleName)
            {
                return Result.NotFound("指定角色不存在");
            }

            var existingRoles = await userManager.GetRolesAsync(user);
            var previousRoleName = existingRoles
                .OrderBy(role => role, StringComparer.Ordinal)
                .FirstOrDefault();
            var alreadyInTargetRole = existingRoles.Contains(
                targetRoleName,
                StringComparer.OrdinalIgnoreCase);
            var rolesToRemove = existingRoles
                .Where(role => !string.Equals(
                    role,
                    targetRoleName,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (!string.Equals(
                    targetRoleName,
                    IdentityRoleNames.Admin,
                    StringComparison.OrdinalIgnoreCase) &&
                await enabledAdminInvariant.IsLastEnabledAdminAsync(user, existingRoles))
            {
                rejectionAudit = new AuditLogWriteRequest(
                    AuditActionGroups.Identity,
                    "Identity.UpdateUserRole",
                    "User",
                    user.Id.ToString(),
                    user.UserName ?? string.Empty,
                    AuditResults.Rejected,
                    $"拒绝调整用户角色：{user.UserName}，原因是系统至少需要保留 1 个启用中的管理员。",
                    ["roleName"]);
                return Result.Invalid(IdentityProblemDescriptors.LastEnabledAdminRequired());
            }

            if (rolesToRemove.Length > 0)
            {
                var removeResult = await userManager.RemoveFromRolesAsync(user, rolesToRemove);
                if (!removeResult.Succeeded)
                {
                    return Result.Failure(removeResult.Errors);
                }
            }

            if (!alreadyInTargetRole)
            {
                var addToRoleResult = await userManager.AddToRoleAsync(user, targetRoleName);
                if (!addToRoleResult.Succeeded)
                {
                    return Result.Failure(addToRoleResult.Errors);
                }
            }

            var roleChanged = rolesToRemove.Length > 0 || !alreadyInTargetRole;
            if (roleChanged)
            {
                IdentityGovernanceHelper.RefreshSecurityStamp(user);
                var updateSecurityStampResult = await userManager.UpdateAsync(user);
                if (!updateSecurityStampResult.Succeeded)
                {
                    return Result.Failure(updateSecurityStampResult.Errors);
                }
            }

            var changedFields = roleChanged ? ["roleName"] : Array.Empty<string>();

            await auditLogWriter.WriteAsync(
                new AuditLogWriteRequest(
                    AuditActionGroups.Identity,
                    "Identity.UpdateUserRole",
                    "User",
                    user.Id.ToString(),
                    user.UserName ?? string.Empty,
                    AuditResults.Succeeded,
                    previousRoleName == null
                        ? $"调整用户角色：{user.UserName}，当前角色为 {targetRoleName}"
                        : $"调整用户角色：{user.UserName}，由 {previousRoleName} 变更为 {targetRoleName}",
                    changedFields),
                cancellationToken);
            return Result.Success(new UserSummaryDto(
                user.Id.ToString(),
                user.UserName ?? string.Empty,
                targetRoleName,
                !IdentityGovernanceHelper.IsUserDisabled(user),
                IdentityGovernanceHelper.GetUserStatus(user)));
        }, cancellationToken);

        if (rejectionAudit is not null)
        {
            await transactionalExecutionService.CommitRejectedAuditAsync(
                auditLogWriter,
                rejectionAudit,
                cancellationToken);
        }

        return result;
    }
}
