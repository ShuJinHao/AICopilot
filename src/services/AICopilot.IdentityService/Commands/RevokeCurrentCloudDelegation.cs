using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.IdentityService.Commands;

public sealed record RevokeCurrentCloudDelegationCommand : ICommand<Result>;

public sealed class RevokeCurrentCloudDelegationCommandHandler(
    ICurrentUser currentUser,
    ICloudDelegationGrantStore grantStore,
    IIdentityAuditLogWriter auditLogWriter,
    ITransactionalExecutionService transactionalExecutionService,
    TimeProvider timeProvider)
    : ICommandHandler<RevokeCurrentCloudDelegationCommand, Result>
{
    public Task<Result> Handle(
        RevokeCurrentCloudDelegationCommand command,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.Id.HasValue)
        {
            return Task.FromResult(Result.Unauthorized(
                new ApiProblemDescriptor(
                    AuthProblemCodes.SessionRevoked,
                    "当前登录态无效，请重新登录。")));
        }

        if (string.IsNullOrWhiteSpace(currentUser.CloudDelegationId))
        {
            return string.Equals(
                    currentUser.IdentityProvider,
                    ExternalIdentityProviders.Cloud,
                    StringComparison.Ordinal)
                ? Task.FromResult(Result.Unauthorized(
                    new ApiProblemDescriptor(
                        AuthProblemCodes.SessionRevoked,
                        "当前 Cloud 登录态缺少有效委托，请重新登录。")))
                : Task.FromResult(Result.Success());
        }

        if (!Guid.TryParse(currentUser.CloudDelegationId, out var grantId) ||
            grantId == Guid.Empty)
        {
            return Task.FromResult(Result.Unauthorized(
                new ApiProblemDescriptor(
                    AuthProblemCodes.SessionRevoked,
                    "当前 Cloud 登录态中的委托标识无效，请重新登录。")));
        }

        return transactionalExecutionService.ExecuteResultAsync(
            async ct =>
            {
                var revoked = await grantStore.RevokeCurrentAsync(
                    grantId,
                    currentUser.Id.Value,
                    timeProvider.GetUtcNow().UtcDateTime,
                    ct);
                if (!revoked.Found)
                {
                    return Result.Unauthorized(
                        new ApiProblemDescriptor(
                            AuthProblemCodes.SessionRevoked,
                            "当前 Cloud 委托不存在或不属于当前用户，请重新登录。"));
                }

                await auditLogWriter.WriteAsync(
                    new AuditLogWriteRequest(
                        AuditActionGroups.Identity,
                        "Identity.CloudDelegationRevokeCurrent",
                        "CloudDelegationGrant",
                        grantId.ToString("D"),
                        currentUser.UserName ?? "CurrentUser",
                        AuditResults.Succeeded,
                        revoked.AlreadyRevoked
                            ? "当前 Cloud 委托此前已撤销，本次注销幂等完成。"
                            : "当前 Cloud 委托已撤销，受保护 Token 已立即清除。",
                        revoked.AlreadyRevoked
                            ? []
                            : ["revokedAtUtc", "protectedToken"],
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["identityProvider"] = ExternalIdentityProviders.Cloud,
                            ["alreadyRevoked"] = revoked.AlreadyRevoked.ToString().ToLowerInvariant()
                        }),
                    ct);

                return Result.Success();
            },
            cancellationToken);
    }
}
