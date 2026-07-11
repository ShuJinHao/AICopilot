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
        return await transactionalExecutionService.ExecuteResultAsync(async _ =>
        {
            var normalizedUserName = command.UserName.Trim();
            var normalizedRoleName = command.RoleName.Trim();
            var role = await roleManager.FindByNameAsync(normalizedRoleName);
            if (role?.Name is not { } canonicalRoleName)
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

            var roleResult = await userManager.AddToRoleAsync(user, canonicalRoleName);
            if (!roleResult.Succeeded)
            {
                return Result.Failure(roleResult.Errors);
            }

            await auditLogWriter.WriteAsync(
                new AuditLogWriteRequest(
                    AuditActionGroups.Identity,
                    "Identity.CreateUser",
                    "User",
                    user.Id.ToString(),
                    user.UserName!,
                    AuditResults.Succeeded,
                    $"创建用户：{user.UserName}，初始角色为 {canonicalRoleName}",
                    ["userName", "roleName"]),
                cancellationToken);
            return Result.Success(new CreatedUserDto(
                user.Id.ToString(),
                user.UserName!,
                canonicalRoleName,
                true,
                UserGovernanceStatuses.Enabled));
        }, cancellationToken);
    }

}
