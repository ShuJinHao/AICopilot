using System.Security.Claims;
using AICopilot.IdentityService.Authorization;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AICopilot.IdentityService.Services;

public sealed class CloudIdentityStatusValidator(
    IOptions<CloudIdentityStatusOptions> options,
    ICloudIdentityStatusClient statusClient,
    IExternalIdentityBindingStore bindingStore,
    UserManager<ApplicationUser> userManager,
    IIdentityAuditLogWriter auditLogWriter,
    ITransactionalExecutionService transactionalExecutionService,
    ICloudIdentityStatusValidationCache validationCache) : ICloudIdentityStatusValidator
{
    public async Task<CloudIdentityStatusValidationResult> ValidateAsync(
        ApplicationUser user,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var configuredOptions = options.Value;
        if (!configuredOptions.Enabled)
        {
            return CloudIdentityStatusValidationResult.Valid();
        }

        if (!IsCloudIdentity(principal))
        {
            return CloudIdentityStatusValidationResult.Valid();
        }

        var tokenContext = TryCreateTokenContext(principal);
        if (tokenContext is null)
        {
            await WriteRejectedAuditAsync(
                user,
                null,
                "missing-cloud-identity-claims",
                "Cloud 身份 token 缺少状态校验所需声明，拒绝当前登录态。",
                cancellationToken);

            return CloudIdentityStatusValidationResult.Failure(
                AuthProblemCodes.CloudIdentityUnverified,
                "Cloud 身份状态无法核验，请重新登录。");
        }

        var now = DateTimeOffset.UtcNow;
        if (validationCache.TryGetSuccess(
                tokenContext.TenantId,
                tokenContext.CloudUserId,
                tokenContext.CloudStatusVersion,
                now))
        {
            return CloudIdentityStatusValidationResult.Valid();
        }

        var check = await statusClient.GetStatusAsync(
            tokenContext.CloudUserId,
            tokenContext.TenantId,
            cancellationToken);

        if (check.Outcome == CloudIdentityStatusCheckOutcome.Unavailable)
        {
            await WriteRejectedAuditAsync(
                user,
                tokenContext,
                "cloud-status-unavailable",
                "Cloud 身份状态接口不可用且无有效缓存，拒绝当前 Cloud 身份请求。",
                cancellationToken);

            return CloudIdentityStatusValidationResult.Failure(
                AuthProblemCodes.CloudIdentityUnverified,
                "Cloud 身份状态暂时无法核验，请稍后重试。");
        }

        if (check.Outcome == CloudIdentityStatusCheckOutcome.NotFound || check.Status is null)
        {
            await RevokeCloudSessionAsync(
                user,
                tokenContext,
                check.Status,
                "cloud-identity-not-found",
                "Cloud 身份不存在，已失效当前 AI 登录态。",
                cancellationToken);

            return CloudIdentityStatusValidationResult.Failure(
                AuthProblemCodes.SessionRevoked,
                "Cloud 身份已失效，请重新登录。");
        }

        var status = check.Status;
        if (!status.AccountEnabled || !status.EmployeeActive)
        {
            await RevokeCloudSessionAsync(
                user,
                tokenContext,
                status,
                status.AccountEnabled ? "cloud-employee-inactive" : "cloud-account-disabled",
                status.AccountEnabled
                    ? "Cloud 员工已失效，已失效当前 AI 登录态。"
                    : "Cloud 账号已禁用，已失效当前 AI 登录态。",
                cancellationToken);

            return CloudIdentityStatusValidationResult.Failure(
                AuthProblemCodes.CloudIdentityInactive,
                status.AccountEnabled
                    ? "Cloud 员工状态无效，请重新登录。"
                    : "Cloud 账号已禁用，请重新登录。");
        }

        if (!string.Equals(status.StatusVersion, tokenContext.CloudStatusVersion, StringComparison.Ordinal))
        {
            await RevokeCloudSessionAsync(
                user,
                tokenContext,
                status,
                "cloud-status-version-changed",
                "Cloud 身份状态版本已变化，已失效当前 AI 登录态。",
                cancellationToken);

            return CloudIdentityStatusValidationResult.Failure(
                AuthProblemCodes.SessionRevoked,
                "Cloud 身份状态已变化，请重新登录。");
        }

        validationCache.StoreSuccess(
            tokenContext.TenantId,
            tokenContext.CloudUserId,
            tokenContext.CloudStatusVersion,
            now.AddSeconds(Math.Clamp(configuredOptions.RefreshIntervalSeconds, 5, 3600)));

        return CloudIdentityStatusValidationResult.Valid();
    }

    private async Task RevokeCloudSessionAsync(
        ApplicationUser user,
        CloudIdentityTokenContext tokenContext,
        CloudIdentityStatusSnapshot? status,
        string rejectionReason,
        string summary,
        CancellationToken cancellationToken)
    {
        validationCache.Remove(tokenContext.TenantId, tokenContext.CloudUserId);

        await transactionalExecutionService.ExecuteAsync(
            async ct =>
            {
                if (status is not null)
                {
                    var binding = await bindingStore.FindByExternalIdentityAsync(
                        ExternalIdentityProviders.Cloud,
                        tokenContext.TenantId,
                        tokenContext.CloudUserId,
                        ct);

                    if (binding is not null)
                    {
                        await bindingStore.UpdateSnapshotAsync(
                            new UpdateExternalIdentityBindingSnapshotRequest(
                                binding.Id,
                                tokenContext.CloudEmployeeId,
                                tokenContext.CloudEmployeeNo,
                                binding.DisplayNameSnapshot,
                                binding.DepartmentIdSnapshot,
                                binding.DepartmentNameSnapshot,
                                status.StatusVersion,
                                status.AccountEnabled,
                                status.EmployeeActive,
                                DateTime.UtcNow),
                            ct);
                    }
                }

                IdentityGovernanceHelper.RefreshSecurityStamp(user);
                var updateResult = await userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    throw new InvalidOperationException("Failed to refresh AICopilot user security stamp.");
                }

                await StageRejectedAuditAsync(user, tokenContext, rejectionReason, summary, ct);
                return true;
            },
            cancellationToken);
    }

    private Task WriteRejectedAuditAsync(
        ApplicationUser user,
        CloudIdentityTokenContext? tokenContext,
        string rejectionReason,
        string summary,
        CancellationToken cancellationToken)
    {
        return transactionalExecutionService.ExecuteAsync(
            async ct =>
            {
                await StageRejectedAuditAsync(user, tokenContext, rejectionReason, summary, ct);
                return true;
            },
            cancellationToken);
    }

    private Task StageRejectedAuditAsync(
        ApplicationUser user,
        CloudIdentityTokenContext? tokenContext,
        string rejectionReason,
        string summary,
        CancellationToken cancellationToken)
    {
        return auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Identity,
                "Identity.CloudStatusRejected",
                "ExternalIdentityBinding",
                user.Id.ToString(),
                user.UserName ?? tokenContext?.CloudEmployeeNo ?? "CloudUser",
                AuditResults.Rejected,
                summary,
                ChangedFields: tokenContext is null
                    ? ["identityProvider", "rejectionReason"]
                    : [
                        "identityProvider",
                        "cloudTenantId",
                        "cloudUserId",
                        "cloudEmployeeNo",
                        "cloudStatusVersion",
                        "rejectionReason"
                    ],
                Metadata: BuildAuditMetadata(tokenContext, rejectionReason)),
            cancellationToken);
    }

    private static bool IsCloudIdentity(ClaimsPrincipal principal)
    {
        return string.Equals(
            principal.FindFirstValue(ExternalIdentityJwtClaimTypes.IdentityProvider),
            ExternalIdentityProviders.Cloud,
            StringComparison.Ordinal);
    }

    private static CloudIdentityTokenContext? TryCreateTokenContext(ClaimsPrincipal principal)
    {
        var tenantId = principal.FindFirstValue(ExternalIdentityJwtClaimTypes.CloudTenantId);
        var cloudUserId = principal.FindFirstValue(ExternalIdentityJwtClaimTypes.CloudUserId);
        var statusVersion = principal.FindFirstValue(ExternalIdentityJwtClaimTypes.CloudStatusVersion);

        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(cloudUserId) ||
            string.IsNullOrWhiteSpace(statusVersion))
        {
            return null;
        }

        return new CloudIdentityTokenContext(
            principal.FindFirstValue(ExternalIdentityJwtClaimTypes.CloudIssuer),
            tenantId.Trim(),
            cloudUserId.Trim(),
            principal.FindFirstValue(ExternalIdentityJwtClaimTypes.CloudEmployeeId),
            principal.FindFirstValue(ExternalIdentityJwtClaimTypes.CloudEmployeeNo),
            statusVersion.Trim());
    }

    private static IReadOnlyDictionary<string, string> BuildAuditMetadata(
        CloudIdentityTokenContext? tokenContext,
        string rejectionReason)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["identityProvider"] = ExternalIdentityProviders.Cloud,
            ["authMethod"] = "CloudOidc",
            ["rejectionReason"] = rejectionReason
        };

        if (tokenContext is null)
        {
            return metadata;
        }

        AddIfPresent(metadata, "cloudIssuer", tokenContext.CloudIssuer);
        metadata["cloudTenantId"] = tokenContext.TenantId;
        metadata["cloudUserId"] = tokenContext.CloudUserId;
        AddIfPresent(metadata, "cloudEmployeeNo", tokenContext.CloudEmployeeNo);
        metadata["cloudStatusVersion"] = tokenContext.CloudStatusVersion;
        return metadata;
    }

    private static void AddIfPresent(Dictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value.Trim();
        }
    }

    private sealed record CloudIdentityTokenContext(
        string? CloudIssuer,
        string TenantId,
        string CloudUserId,
        string? CloudEmployeeId,
        string? CloudEmployeeNo,
        string CloudStatusVersion);
}
