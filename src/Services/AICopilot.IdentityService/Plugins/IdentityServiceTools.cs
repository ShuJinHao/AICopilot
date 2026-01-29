using AICopilot.AgentPlugin;
using AICopilot.IdentityService.Commands;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace AICopilot.IdentityService.Plugins;

public class IdentityServiceTools : AgentPluginBase
{
    [DisplayName("CreateUser")]
    [Description("根据提供的用户名和密码创建一个新用户")]
    public static async Task<bool> CreateUserAsync(IServiceProvider sp,
        [Description("用于注册新用户的用户名")] string userName,
        [Description("用于注册新用户的密码")] string password)
    {
        var sender = sp.GetRequiredService<ISender>();
        var result = await sender.Send(new CreateUserCommand(userName, password));
        return result.IsSuccess;
    }
}