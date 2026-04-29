using AICopilot.IdentityService.Authorization;
using AICopilot.IdentityService.Queries;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;

namespace AICopilot.IdentityService.Commands;

[AuthorizeRequirement("Identity.EnableUser")]
public record EnableUserCommand(string UserId) : ICommand<Result<UserSummaryDto>>;

public sealed class EnableUserCommandHandler(
    UserManager<ApplicationUser> userManager,
    IIdentityAuditLogWriter auditLogWriter,
    ITransactionalExecutionService transactionalExecutionService)
    : ICommandHandler<EnableUserCommand, Result<UserSummaryDto>>
{
    public async Task<Result<UserSummaryDto>> Handle(
        EnableUserCommand command,
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
            if (!IdentityGovernanceHelper.IsUserDisabled(user))
            {
                return Result.Success(BuildSummary(user, roleName));
            }

            IdentityGovernanceHelper.MarkUserEnabled(user);
            IdentityGovernanceHelper.RefreshSecurityStamp(user);

            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return Result.Failure(updateResult.Errors.ToArray());
            }

            await auditLogWriter.WriteAsync(
                new AuditLogWriteRequest(
                    AuditActionGroups.Identity,
                    "Identity.EnableUser",
                    "User",
                    user.Id.ToString(),
                    user.UserName ?? string.Empty,
                    AuditResults.Succeeded,
                    $"恢复启用用户：{user.UserName}",
                    ["status"]),
                cancellationToken);
            return Result.Success(BuildSummary(user, roleName));
        }, cancellationToken);
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
