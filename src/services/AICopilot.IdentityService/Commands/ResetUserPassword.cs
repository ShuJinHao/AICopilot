using AICopilot.IdentityService.Authorization;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;

namespace AICopilot.IdentityService.Commands;

[AuthorizeRequirement("Identity.ResetUserPassword")]
public record ResetUserPasswordCommand(string UserId, string NewPassword) : ICommand<Result>;

public sealed class ResetUserPasswordCommandHandler(
    UserManager<ApplicationUser> userManager,
    IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IIdentityAuditLogWriter auditLogWriter,
    ITransactionalExecutionService transactionalExecutionService)
    : ICommandHandler<ResetUserPasswordCommand, Result>
{
    public async Task<Result> Handle(
        ResetUserPasswordCommand command,
        CancellationToken cancellationToken)
    {
        return await transactionalExecutionService.ExecuteAsync(async _ =>
        {
            var user = await userManager.FindByIdAsync(command.UserId);
            if (user is null)
            {
                return Result.NotFound("用户不存在");
            }

            var validationErrors = new List<IdentityError>();
            foreach (var validator in passwordValidators)
            {
                var validationResult = await validator.ValidateAsync(userManager, user, command.NewPassword);
                if (!validationResult.Succeeded)
                {
                    validationErrors.AddRange(validationResult.Errors);
                }
            }

            if (validationErrors.Count > 0)
            {
                return Result.Failure(validationErrors.ToArray());
            }

            user.PasswordHash = passwordHasher.HashPassword(user, command.NewPassword);
            IdentityGovernanceHelper.RefreshSecurityStamp(user);
            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return Result.Failure(updateResult.Errors.ToArray());
            }

            await auditLogWriter.WriteAsync(
                new AuditLogWriteRequest(
                    AuditActionGroups.Identity,
                    "Identity.ResetUserPassword",
                    "User",
                    user.Id.ToString(),
                    user.UserName ?? string.Empty,
                    AuditResults.Succeeded,
                    $"重置用户密码：{user.UserName}",
                    ["password"]),
                cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }
}
