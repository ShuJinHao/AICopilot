using System.Security.Claims;
using AICopilot.IdentityService.Authorization;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;

namespace AICopilot.IdentityService.Commands;

public sealed record ConfirmExistingCloudOidcAccountCommand(
    CloudOidcIdentityProfile Profile,
    string Password) : ICommand<Result<LoginUserDto>>;

public sealed class ConfirmExistingCloudOidcAccountCommandHandler(
    UserManager<ApplicationUser> userManager,
    IExternalIdentityBindingStore bindingStore,
    IExternalIdentityBindingInvariantGuard bindingInvariantGuard,
    IIdentityAuditLogWriter auditLogWriter,
    IJwtTokenGenerator jwtTokenGenerator,
    ITransactionalExecutionService transactionalExecutionService)
    : ICommandHandler<ConfirmExistingCloudOidcAccountCommand, Result<LoginUserDto>>
{
    public async Task<Result<LoginUserDto>> Handle(
        ConfirmExistingCloudOidcAccountCommand command,
        CancellationToken cancellationToken)
    {
        var profile = NormalizeProfile(command.Profile);
        var rejectionAudit = new RejectionAuditBuffer();

        var result = await transactionalExecutionService.ExecuteResultAsync(
            async ct =>
            {
                rejectionAudit.Clear();
                if (!profile.AccountEnabled || !profile.EmployeeActive)
                {
                    rejectionAudit.Set(CreateRejectedAudit(
                        "Identity.CloudOidcExistingAccountCloudIdentityInactive",
                        profile,
                        "Cloud 账号或员工状态无效，拒绝确认现有 AI 账号。"));
                    return Result.Unauthorized(new ApiProblemDescriptor(
                        AuthProblemCodes.CloudIdentityInactive,
                        "Cloud 账号或员工状态无效，无法登录 AICopilot。"));
                }

                var localUserName = ResolveLocalUserName(profile);
                var user = await userManager.FindByNameAsync(localUserName);
                if (user is null)
                {
                    rejectionAudit.Set(CreateRejectedAudit(
                        "Identity.CloudOidcExistingAccountMissing",
                        profile,
                        "Cloud 身份对应的同名 AI 本地账号不存在，拒绝绑定。"));
                    return Result.Unauthorized(new ApiProblemDescriptor(
                        AuthProblemCodes.ExternalIdentityConflict,
                        "Cloud 身份对应的 AI 本地账号不存在，请重新登录或联系 AI 管理员。"));
                }

                await bindingInvariantGuard.AcquireAsync(
                    ExternalIdentityProviders.Cloud,
                    profile.TenantId,
                    profile.Subject,
                    user.Id,
                    ct);

                user = await userManager.FindByIdAsync(user.Id.ToString());
                if (user is null)
                {
                    rejectionAudit.Set(CreateRejectedAudit(
                        "Identity.CloudOidcExistingAccountMissing",
                        profile,
                        "Cloud 身份对应的同名 AI 本地账号不存在，拒绝绑定。"));
                    return Result.Unauthorized(new ApiProblemDescriptor(
                        AuthProblemCodes.ExternalIdentityConflict,
                        "Cloud 身份对应的 AI 本地账号不存在，请重新登录或联系 AI 管理员。"));
                }

                if (IdentityGovernanceHelper.IsUserDisabled(user))
                {
                    rejectionAudit.Set(CreateRejectedAudit(
                        "Identity.CloudOidcExistingAccountDisabled",
                        profile,
                        "同名 AI 本地账号已禁用，拒绝 Cloud 身份绑定。",
                        user));
                    return Result.Unauthorized(new ApiProblemDescriptor(
                        AuthProblemCodes.AccountDisabled,
                        "AICopilot 本地账号已禁用，请联系 AI 管理员恢复启用。"));
                }

                if (string.IsNullOrEmpty(command.Password) ||
                    !await userManager.CheckPasswordAsync(user, command.Password))
                {
                    rejectionAudit.Set(CreateRejectedAudit(
                        "Identity.CloudOidcExistingAccountPasswordRejected",
                        profile,
                        "同名 AI 本地账号密码确认失败，拒绝 Cloud 身份绑定。",
                        user));
                    return Result.Unauthorized(new ApiProblemDescriptor(
                        AuthProblemCodes.InvalidCredentials,
                        "本地 AI 账号密码无效，请重新输入。"));
                }

                var bindingResult = await ResolveBindingAsync(user, profile, ct);
                if (!bindingResult.IsSuccess)
                {
                    rejectionAudit.Set(CreateRejectedAudit(
                        "Identity.CloudOidcExistingAccountBindingConflict",
                        profile,
                        "Cloud 身份或 AI 本地账号已绑定到其他身份，拒绝覆盖。",
                        user));
                    return Result.Unauthorized(new ApiProblemDescriptor(
                        AuthProblemCodes.ExternalIdentityConflict,
                        "Cloud 身份或本地 AI 账号已存在其他绑定，请联系 AI 管理员处理。"));
                }

                if (string.IsNullOrWhiteSpace(user.SecurityStamp))
                {
                    var stampResult = await userManager.UpdateSecurityStampAsync(user);
                    if (!stampResult.Succeeded)
                    {
                        throw new InvalidOperationException(
                            "Unable to initialize the confirmed Cloud-bound user's security stamp.");
                    }

                    user = await userManager.FindByIdAsync(user.Id.ToString())
                        ?? throw new InvalidOperationException(
                            $"User '{user.Id}' was not found after updating security stamp.");
                }

                var token = await GenerateAiTokenAsync(user, profile, ct);
                await auditLogWriter.WriteAsync(
                    new AuditLogWriteRequest(
                        AuditActionGroups.Identity,
                        "Identity.CloudOidcExistingAccountConfirmed",
                        "ExternalIdentityBinding",
                        user.Id.ToString(),
                        user.UserName ?? localUserName,
                        AuditResults.Succeeded,
                        bindingResult.Value!
                            ? "Cloud 身份已由本地密码确认并绑定到现有 AI 账号。"
                            : "Cloud 身份再次由本地密码确认，复用现有 AI 账号绑定。",
                        BuildChangedFields(profile, bindingResult.Value!),
                        BuildAuditMetadata(profile)),
                    ct);

                return Result.Success(new LoginUserDto(user.UserName!, token));
            },
            cancellationToken);

        if (!result.IsSuccess && rejectionAudit.Request is not null)
        {
            await transactionalExecutionService.CommitRejectedAuditAsync(
                auditLogWriter,
                rejectionAudit.Request,
                cancellationToken);
        }

        return result;
    }

    private async Task<Result<bool>> ResolveBindingAsync(
        ApplicationUser user,
        CloudOidcIdentityProfile profile,
        CancellationToken cancellationToken)
    {
        var externalBinding = await bindingStore.FindByExternalIdentityAsync(
            ExternalIdentityProviders.Cloud,
            profile.TenantId,
            profile.Subject,
            cancellationToken);
        var userBinding = await bindingStore.FindByUserProviderAsync(
            user.Id,
            ExternalIdentityProviders.Cloud,
            cancellationToken);

        if (externalBinding is not null && externalBinding.UserId != user.Id)
        {
            return Result.Failure();
        }

        if (userBinding is not null &&
            (!string.Equals(userBinding.TenantId, profile.TenantId, StringComparison.Ordinal) ||
             !string.Equals(userBinding.ExternalUserId, profile.Subject, StringComparison.Ordinal)))
        {
            return Result.Failure();
        }

        var existingBinding = externalBinding ?? userBinding;
        if (existingBinding is not null)
        {
            await bindingStore.UpdateSnapshotAsync(
                new UpdateExternalIdentityBindingSnapshotRequest(
                    existingBinding.Id,
                    profile.EmployeeId,
                    profile.EmployeeNo,
                    profile.DisplayName,
                    profile.DepartmentId,
                    profile.DepartmentName,
                    profile.StatusVersion,
                    profile.AccountEnabled,
                    profile.EmployeeActive,
                    DateTime.UtcNow),
                cancellationToken);
            return Result.Success(false);
        }

        await bindingStore.CreateAsync(
            new CreateExternalIdentityBindingRequest(
                user.Id,
                ExternalIdentityProviders.Cloud,
                profile.TenantId,
                profile.Subject,
                profile.EmployeeId,
                profile.EmployeeNo,
                profile.DisplayName,
                profile.DepartmentId,
                profile.DepartmentName,
                profile.StatusVersion,
                profile.AccountEnabled,
                profile.EmployeeActive,
                DateTime.UtcNow),
            cancellationToken);
        return Result.Success(true);
    }

    private async Task<string> GenerateAiTokenAsync(
        ApplicationUser user,
        CloudOidcIdentityProfile profile,
        CancellationToken cancellationToken)
    {
        var userClaims = await userManager.GetClaimsAsync(user);
        var userRoles = await userManager.GetRolesAsync(user);
        return await jwtTokenGenerator.GenerateTokenAsync(
            new JwtTokenUser(
                user.Id,
                user.UserName!,
                user.SecurityStamp ?? string.Empty,
                userRoles.ToArray(),
                userClaims.Concat(BuildCloudJwtClaims(profile)).ToArray()),
            cancellationToken);
    }

    private static AuditLogWriteRequest CreateRejectedAudit(
        string actionCode,
        CloudOidcIdentityProfile profile,
        string summary,
        ApplicationUser? user = null)
    {
        return new AuditLogWriteRequest(
            AuditActionGroups.Identity,
            actionCode,
            "ExternalIdentityBinding",
            user?.Id.ToString() ?? $"{profile.TenantId}:{profile.Subject}",
            user?.UserName ?? profile.PreferredUserName,
            AuditResults.Rejected,
            summary,
            BuildChangedFields(profile, includeBindingFields: false),
            BuildAuditMetadata(profile, actionCode));
    }

    private static Claim[] BuildCloudJwtClaims(CloudOidcIdentityProfile profile)
    {
        var claims = new List<Claim>
        {
            new(ExternalIdentityJwtClaimTypes.IdentityProvider, ExternalIdentityProviders.Cloud),
            new(ExternalIdentityJwtClaimTypes.CloudIssuer, profile.Issuer),
            new(ExternalIdentityJwtClaimTypes.CloudTenantId, profile.TenantId),
            new(ExternalIdentityJwtClaimTypes.CloudUserId, profile.Subject)
        };

        AddClaimIfPresent(claims, ExternalIdentityJwtClaimTypes.CloudEmployeeId, profile.EmployeeId);
        AddClaimIfPresent(claims, ExternalIdentityJwtClaimTypes.CloudEmployeeNo, profile.EmployeeNo);
        AddClaimIfPresent(claims, ExternalIdentityJwtClaimTypes.CloudDepartmentId, profile.DepartmentId);
        AddClaimIfPresent(claims, ExternalIdentityJwtClaimTypes.CloudDepartmentName, profile.DepartmentName);
        AddClaimIfPresent(claims, ExternalIdentityJwtClaimTypes.CloudStatusVersion, profile.StatusVersion);
        return claims.ToArray();
    }

    private static IReadOnlyCollection<string> BuildChangedFields(
        CloudOidcIdentityProfile profile,
        bool includeBindingFields)
    {
        var fields = new List<string>
        {
            "identityProvider",
            "cloudIssuer",
            "cloudTenantId",
            "cloudUserId",
            "accountEnabled",
            "employeeActive"
        };

        AddFieldIfPresent(fields, "employeeId", profile.EmployeeId);
        AddFieldIfPresent(fields, "employeeNo", profile.EmployeeNo);
        AddFieldIfPresent(fields, "displayName", profile.DisplayName);
        AddFieldIfPresent(fields, "departmentId", profile.DepartmentId);
        AddFieldIfPresent(fields, "departmentName", profile.DepartmentName);
        AddFieldIfPresent(fields, "statusVersion", profile.StatusVersion);
        if (includeBindingFields)
        {
            fields.Add("userId");
        }

        return fields;
    }

    private static IReadOnlyDictionary<string, string> BuildAuditMetadata(
        CloudOidcIdentityProfile profile,
        string? rejectionReason = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["identityProvider"] = ExternalIdentityProviders.Cloud,
            ["cloudIssuer"] = profile.Issuer,
            ["cloudTenantId"] = profile.TenantId,
            ["cloudUserId"] = profile.Subject,
            ["authMethod"] = "CloudOidcLocalPasswordConfirmation"
        };

        AddMetadataIfPresent(metadata, "cloudEmployeeNo", profile.EmployeeNo);
        AddMetadataIfPresent(metadata, "cloudStatusVersion", profile.StatusVersion);
        AddMetadataIfPresent(metadata, "rejectionReason", rejectionReason);
        return metadata;
    }

    private static string ResolveLocalUserName(CloudOidcIdentityProfile profile)
    {
        return FirstNonEmpty(profile.EmployeeNo, profile.PreferredUserName, profile.Subject);
    }

    private static CloudOidcIdentityProfile NormalizeProfile(CloudOidcIdentityProfile profile)
    {
        return profile with
        {
            Issuer = NormalizeRequired(profile.Issuer),
            Subject = NormalizeRequired(profile.Subject),
            TenantId = string.IsNullOrWhiteSpace(profile.TenantId)
                ? CloudOidcIdentityProfile.DefaultTenantId
                : profile.TenantId.Trim(),
            PreferredUserName = FirstNonEmpty(profile.PreferredUserName, profile.EmployeeNo, profile.Subject),
            DisplayName = EmptyToNull(profile.DisplayName),
            EmployeeId = EmptyToNull(profile.EmployeeId),
            EmployeeNo = EmptyToNull(profile.EmployeeNo),
            DepartmentId = EmptyToNull(profile.DepartmentId),
            DepartmentName = EmptyToNull(profile.DepartmentName),
            StatusVersion = EmptyToNull(profile.StatusVersion)
        };
    }

    private static string NormalizeRequired(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Cloud OIDC profile contains an empty required field.");
        }

        return value.Trim();
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void AddClaimIfPresent(List<Claim> claims, string claimType, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            claims.Add(new Claim(claimType, value));
        }
    }

    private static void AddFieldIfPresent(List<string> fields, string fieldName, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.Add(fieldName);
        }
    }

    private static void AddMetadataIfPresent(
        Dictionary<string, string> metadata,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value.Trim();
        }
    }

    private sealed class RejectionAuditBuffer
    {
        public AuditLogWriteRequest? Request { get; private set; }

        public void Clear()
        {
            Request = null;
        }

        public void Set(AuditLogWriteRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (Request is not null)
            {
                throw new InvalidOperationException(
                    "Cloud OIDC existing-account confirmation produced more than one rejection audit in a single attempt.");
            }

            Request = request;
        }
    }
}
