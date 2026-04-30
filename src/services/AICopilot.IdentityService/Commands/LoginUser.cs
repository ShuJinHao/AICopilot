using AICopilot.IdentityService.Authorization;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;

namespace AICopilot.IdentityService.Commands;

public record LoginUserDto(string UserName, string Token);

public record LoginUserCommand(string UserName, string Password) : ICommand<Result<LoginUserDto>>;

public class LoginUserCommandHandler(
    UserManager<ApplicationUser> userManager,
    IJwtTokenGenerator jwtTokenGenerator)
    : ICommandHandler<LoginUserCommand, Result<LoginUserDto>>
{
    public async Task<Result<LoginUserDto>> Handle(LoginUserCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByNameAsync(command.UserName);
        if (user == null)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.InvalidCredentials,
                "用户名或密码无效。"));
        }

        if (IdentityGovernanceHelper.IsUserDisabled(user))
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.AccountDisabled,
                "账号已禁用，请联系管理员恢复启用。"));
        }

        var result = await userManager.CheckPasswordAsync(user, command.Password);
        if (!result)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.InvalidCredentials,
                "用户名或密码无效。"));
        }

        if (string.IsNullOrWhiteSpace(user.SecurityStamp))
        {
            await userManager.UpdateSecurityStampAsync(user);
            user = await userManager.FindByIdAsync(user.Id.ToString())
                ?? throw new InvalidOperationException($"User '{user.Id}' was not found after updating security stamp.");
        }

        var userClaims = await userManager.GetClaimsAsync(user);
        var userRoles = await userManager.GetRolesAsync(user);
        var token = await jwtTokenGenerator.GenerateTokenAsync(
            new JwtTokenUser(
                user.Id,
                user.UserName!,
                user.SecurityStamp ?? string.Empty,
                userRoles.ToArray(),
                userClaims.ToArray()),
            cancellationToken);

        return Result.Success(new LoginUserDto(user.UserName!, token));
    }
}
