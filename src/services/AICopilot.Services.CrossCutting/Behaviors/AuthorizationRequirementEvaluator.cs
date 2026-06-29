using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.CrossCutting.Exceptions;
using AICopilot.SharedKernel.Result;

namespace AICopilot.Services.CrossCutting.Behaviors;

public interface IAuthorizationRequirementEvaluator
{
    Task AuthorizeAsync(Type requestType, CancellationToken cancellationToken);
}

public sealed class AuthorizationRequirementEvaluator(IServiceProvider serviceProvider) : IAuthorizationRequirementEvaluator
{
    public async Task AuthorizeAsync(Type requestType, CancellationToken cancellationToken)
    {
        var requiredPermissions = requestType
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), true)
            .Cast<AuthorizeRequirementAttribute>()
            .Select(attribute => attribute.Permission)
            .ToArray();

        if (requiredPermissions.Length == 0)
        {
            return;
        }

        var user = serviceProvider.GetService(typeof(ICurrentUser)) as ICurrentUser;
        if (user is null || !user.IsAuthenticated || user.Id is null)
        {
            throw new UnauthorizedProblemException(new ApiProblemDescriptor(
                AuthProblemCodes.SessionRevoked,
                "当前登录态无效，请重新登录。"));
        }

        var identityAccessService = serviceProvider.GetService(typeof(IIdentityAccessService)) as IIdentityAccessService;
        if (identityAccessService is null)
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
    }
}
