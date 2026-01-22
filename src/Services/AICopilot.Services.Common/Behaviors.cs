using AICopilot.Services.Common.Attributes;
using AICopilot.Services.Common.Exceptions;
using AICopilot.Services.Contracts;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace AICopilot.Services.Common;

public class AuthorizationBehavior<TRequest, TResponse>(ICurrentUser user) :
    IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requiredPermissions = typeof(TRequest)
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), true)
            .Cast<AuthorizeRequirementAttribute>()
            .Select(a => a.Permission)
            .ToList();

        if (requiredPermissions.Count == 0)
            return await next(cancellationToken);

        // 1. 用户是否已认证
        if (!user.IsAuthenticated) throw new ForbiddenException("用户未登录");

        // 2. 获取这些角色包含的权限（可以从数据库查询）
        var userPermissions = LoadPermissions(user.Role!);

        // 4. 权限校验
        if (!requiredPermissions.All(p => userPermissions.Contains(p)))
            throw new ForbiddenException("未授权访问");

        return await next(cancellationToken);
    }

    private List<string> LoadPermissions(string role)
    {
        var permissions = new Dictionary<string, List<string>>()
        {
            ["Admin"] = ["Identity.CreateRole"],
            ["User"] = []
        };

        return permissions[role];
    }
}