using AICopilot.IdentityService.Authorization;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;

namespace AICopilot.IdentityService.Commands;

public record CreatedUserDto(
    string UserId,
    string UserName,
    string RoleName,
    bool IsEnabled,
    string Status);

[AuthorizeRequirement("Identity.CreateUser")]
public record CreateUserCommand(string UserName, string Password, string RoleName)
    : ICommand<Result<CreatedUserDto>>;

public sealed class CreateUserCommandHandler(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IIdentityAuditLogWriter auditLogWriter,
    ITransactionalExecutionService transactionalExecutionService) : ICommandHandler<CreateUserCommand, Result<CreatedUserDto>>
{
    public async Task<Result<CreatedUserDto>> Handle(
        CreateUserCommand command,
        CancellationToken cancellationToken)
    {
        return await transactionalExecutionService.ExecuteAsync(async _ =>
        {
            var normalizedUserName = command.UserName.Trim();
            var normalizedRoleName = command.RoleName.Trim();

            if (!await roleManager.RoleExistsAsync(normalizedRoleName))
            {
                return Result.NotFound("指定角色不存在");
            }

            if (await userManager.FindByNameAsync(normalizedUserName) is not null)
            {
                return Result.Invalid("用户名已存在");
            }

            var user = new ApplicationUser
            {
                UserName = normalizedUserName
            };

            var result = await userManager.CreateAsync(user, command.Password);
            if (!result.Succeeded)
            {
                return Result.Failure(result.Errors);
            }

            await SetSingleRoleAsync(user, normalizedRoleName);

            await auditLogWriter.WriteAsync(
                new AuditLogWriteRequest(
                    AuditActionGroups.Identity,
                    "Identity.CreateUser",
                    "User",
                    user.Id.ToString(),
                    user.UserName!,
                    AuditResults.Succeeded,
                    $"创建用户：{user.UserName}，初始角色为 {normalizedRoleName}",
                    ["userName", "roleName"]),
                cancellationToken);
            return Result.Success(new CreatedUserDto(
                user.Id.ToString(),
                user.UserName!,
                normalizedRoleName,
                true,
                UserGovernanceStatuses.Enabled));
        }, cancellationToken);
    }

    private async Task SetSingleRoleAsync(ApplicationUser user, string roleName)
    {
        var existingRoles = await userManager.GetRolesAsync(user);
        if (existingRoles.Count > 0)
        {
            await userManager.RemoveFromRolesAsync(user, existingRoles);
        }

        await userManager.AddToRoleAsync(user, roleName);
    }
}
