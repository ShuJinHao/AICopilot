using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Exceptions;
using AICopilot.SharedKernel.Result;
using MediatR;

namespace AICopilot.Services.CrossCutting.Behaviors;

public class AuthorizationBehavior<TRequest, TResponse>(
    ICurrentUser user,
    IIdentityAccessService identityAccessService) :
    IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requiredPermissions = typeof(TRequest)
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), true)
            .Cast<AuthorizeRequirementAttribute>()
            .Select(attribute => attribute.Permission)
            .ToArray();

        if (requiredPermissions.Length == 0)
        {
            return await next(cancellationToken);
        }

        if (!user.IsAuthenticated || user.Id is null)
        {
            throw new UnauthorizedProblemException(new ApiProblemDescriptor(
                AuthProblemCodes.SessionRevoked,
                "当前登录态无效，请重新登录。"));
        }

        var currentUserAccess = await identityAccessService.GetCurrentUserAccessAsync(
            user.Id.Value,
            cancellationToken);

        if (currentUserAccess is null)
        {
            throw new UnauthorizedProblemException(new ApiProblemDescriptor(
                AuthProblemCodes.UserMissing,
                "当前用户不存在，请重新登录。"));
        }

        var missingPermissions = requiredPermissions
            .Where(permission => !currentUserAccess.Permissions.Contains(permission))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (missingPermissions.Length > 0)
        {
            throw new ForbiddenException(new ApiProblemDescriptor(
                AuthProblemCodes.MissingPermission,
                "当前账号没有执行该操作的权限。",
                new Dictionary<string, object?>
                {
                    [ApiProblemExtensionKeys.MissingPermissions] = missingPermissions
                }));
        }

        return await next(cancellationToken);
    }
}
