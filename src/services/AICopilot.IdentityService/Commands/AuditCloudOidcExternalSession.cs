using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;

namespace AICopilot.IdentityService.Commands;

public enum CloudOidcExternalSessionAuditReason
{
    Cancelled,
    InvalidOrExpired
}

public sealed record AuditCloudOidcExternalSessionCommand(
    CloudOidcExternalSessionAuditReason Reason,
    CloudOidcIdentityProfile? Profile) : ICommand<Result>;

public sealed class AuditCloudOidcExternalSessionCommandHandler(
    IIdentityAuditLogWriter auditLogWriter,
    ITransactionalExecutionService transactionalExecutionService)
    : ICommandHandler<AuditCloudOidcExternalSessionCommand, Result>
{
    public Task<Result> Handle(
        AuditCloudOidcExternalSessionCommand command,
        CancellationToken cancellationToken)
    {
        return transactionalExecutionService.ExecuteResultAsync(
            async ct =>
            {
                var profile = command.Profile;
                var actionCode = command.Reason switch
                {
                    CloudOidcExternalSessionAuditReason.Cancelled =>
                        "Identity.CloudOidcConfirmationCancelled",
                    CloudOidcExternalSessionAuditReason.InvalidOrExpired =>
                        "Identity.CloudOidcExternalSessionInvalid",
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(command),
                        command.Reason,
                        null)
                };
                var summary = command.Reason switch
                {
                    CloudOidcExternalSessionAuditReason.Cancelled =>
                        "用户取消 Cloud OIDC 本地账号确认，外部登录会话已清除。",
                    CloudOidcExternalSessionAuditReason.InvalidOrExpired =>
                        "Cloud OIDC 外部登录会话无效或已过期，拒绝换取 AI 登录态。",
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(command),
                        command.Reason,
                        null)
                };
                var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["identityProvider"] = ExternalIdentityProviders.Cloud,
                    ["authMethod"] = "CloudOidc",
                    ["rejectionReason"] = actionCode
                };
                if (profile is not null)
                {
                    metadata["cloudIssuer"] = profile.Issuer;
                    metadata["cloudTenantId"] = profile.TenantId;
                    metadata["cloudUserId"] = profile.Subject;
                }

                await auditLogWriter.WriteAsync(
                    new AuditLogWriteRequest(
                        AuditActionGroups.Identity,
                        actionCode,
                        "ExternalIdentitySession",
                        profile is null ? null : $"{profile.TenantId}:{profile.Subject}",
                        profile?.PreferredUserName ?? "CloudOidcExternalSession",
                        AuditResults.Rejected,
                        summary,
                        ChangedFields: ["externalCookie"],
                        metadata),
                    ct);
                return Result.Success();
            },
            cancellationToken);
    }
}
