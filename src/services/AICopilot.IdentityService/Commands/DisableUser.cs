using AICopilot.IdentityService.Authorization;
using AICopilot.IdentityService.Queries;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;

namespace AICopilot.IdentityService.Commands;

[AuthorizeRequirement("Identity.DisableUser")]
public record DisableUserCommand(string UserId) : ICommand<Result<UserSummaryDto>>;

public sealed class DisableUserCommandHandler(
    UserManager<ApplicationUser> userManager,
    IIdentityAuditLogWriter auditLogWriter,
    ITransactionalExecutionService transactionalExecutionService)
    : ICommandHandler<DisableUserCommand, Result<UserSummaryDto>>
{
    public async Task<Result<UserSummaryDto>> Handle(
        DisableUserCommand command,
        CancellationToken cancellationToken)
    {
        return await transactionalExecutionService.ExecuteAsync(async _ =>
        {
            var user = await userManager.FindByIdAsync(command.UserId);
            if (user is null)
            {
                return Result.NotFound("用户不存在");
            }

            var roleName = await GetSingleRoleNameAsync(user);
            if (IdentityGovernanceHelper.IsUserDisabled(user))
            {
                return Result.Success(BuildSummary(user, roleName));
            }

            if (string.Equals(roleName, IdentityRoleNames.Admin, StringComparison.Ordinal))
            {
                var enabledAdminCount = await CountEnabledUsersInRoleAsync(IdentityRoleNames.Admin);
                if (enabledAdminCount <= 1)
                {
                    await auditLogWriter.WriteAsync(
                        new AuditLogWriteRequest(
                            AuditActionGroups.Identity,
                            "Identity.DisableUser",
                            "User",
                            user.Id.ToString(),
                            user.UserName ?? string.Empty,
                            AuditResults.Rejected,
                            $"拒绝禁用用户：{user.UserName}，原因是系统至少需要保留 1 个启用中的管理员。",
                            ["status"]),
                        cancellationToken);
                    return Result.Invalid("至少保留 1 个启用状态的管理员，不能禁用最后一个管理员账号。");
                }
            }

            IdentityGovernanceHelper.MarkUserDisabled(user);
            IdentityGovernanceHelper.RefreshSecurityStamp(user);

            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return Result.Failure(updateResult.Errors.ToArray());
            }

            await auditLogWriter.WriteAsync(
                new AuditLogWriteRequest(
                    AuditActionGroups.Identity,
                    "Identity.DisableUser",
                    "User",
                    user.Id.ToString(),
                    user.UserName ?? string.Empty,
                    AuditResults.Succeeded,
                    $"禁用用户：{user.UserName}",
                    ["status"]),
                cancellationToken);
            return Result.Success(BuildSummary(user, roleName));
        }, cancellationToken);
    }

    private async Task<int> CountEnabledUsersInRoleAsync(string roleName)
    {
        var users = await userManager.GetUsersInRoleAsync(roleName);
        return users.Count(user => !IdentityGovernanceHelper.IsUserDisabled(user));
    }

    private async Task<string?> GetSingleRoleNameAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return roles
            .OrderBy(role => role, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static UserSummaryDto BuildSummary(ApplicationUser user, string? roleName)
    {
        return new UserSummaryDto(
            user.Id.ToString(),
            user.UserName ?? string.Empty,
            roleName,
            !IdentityGovernanceHelper.IsUserDisabled(user),
            IdentityGovernanceHelper.GetUserStatus(user));
    }
}
