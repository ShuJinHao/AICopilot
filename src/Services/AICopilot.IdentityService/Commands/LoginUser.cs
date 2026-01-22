using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace AICopilot.IdentityService.Commands;

public record LoginUserDto(string UserName, string Token);

public record LoginUserCommand(string UserName, string Password) : ICommand<Result<LoginUserDto>>;

public class LoginUserCommandHandler(
    UserManager<IdentityUser> userManager,
    IJwtTokenGenerator jwtTokenGenerator)
    : ICommandHandler<LoginUserCommand, Result<LoginUserDto>>
{
    public async Task<Result<LoginUserDto>> Handle(LoginUserCommand command, CancellationToken cancellationToken)
    {
        // 1. 查找用户
        var user = await userManager.FindByNameAsync(command.UserName);
        if (user == null)
        {
            return Result.Unauthorized("用户名或密码无效。");
        }

        // 2. 验证密码
        var result = await userManager.CheckPasswordAsync(user, command.Password);
        if (!result)
        {
            return Result.Unauthorized("用户名或密码无效。");
        }

        // 3. 登录成功，生成 Token
        var token = await jwtTokenGenerator.GenerateTokenAsync(user);

        // 4. 返回结果
        return Result.Success(new LoginUserDto(user.UserName!, token));
    }
}