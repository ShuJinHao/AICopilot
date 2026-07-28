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
        var localUserName = ResolveLocalUserName(profile);
        var normalizedUserName = userManager.NormalizeName(localUserName);
        if (string.IsNullOrWhiteSpace(normalizedUserName))
        {
            throw new InvalidOperationException(
                "Cloud OIDC profile did not produce a normalized AICopilot user name.");
        }

        var rejectionAudit = new RejectionAuditBuffer();
        Result<LoginUserDto> result;
        try
        {
            result = await transactionalExecutionService.ExecuteResultAsync(
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

                    var userBeforeLock = await userManager.FindByNameAsync(localUserName);
                    var bindingBeforeLock = await bindingStore.FindByExternalIdentityAsync(
                        ExternalIdentityProviders.Cloud,
                        profile.TenantId,
                        profile.Subject,
                        ct);
                    var knownUserIds = new[] { userBeforeLock?.Id, bindingBeforeLock?.UserId }
                        .Where(userId => userId.HasValue)
                        .Select(userId => userId!.Value)
                        .Distinct()
                        .ToArray();

                    await bindingInvariantGuard.AcquireAsync(
                        new ExternalIdentityBindingInvariantScope(
                            ExternalIdentityProviders.Cloud,
                            profile.TenantId,
                            profile.Subject,
                            normalizedUserName,
                            knownUserIds),
                        ct);

                    var user = await userManager.FindByNameAsync(localUserName);
                    var externalBinding = await bindingStore.FindByExternalIdentityAsync(
                        ExternalIdentityProviders.Cloud,
                        profile.TenantId,
                        profile.Subject,
                        ct);
                    if (user is null)
                    {
                        var actionCode = externalBinding is null
                            ? "Identity.CloudOidcExistingAccountMissing"
                            : "Identity.CloudOidcExternalIdentityBoundToDifferentUser";
                        var detail = externalBinding is null
                            ? "Cloud 身份对应的同名 AI 本地账号已不存在，请重新从 Cloud 登录。"
                            : "该 Cloud 身份已绑定到另一个 AICopilot 本地账号，不能确认当前用户名。";
                        rejectionAudit.Set(CreateRejectedAudit(
                            actionCode,
                            profile,
                            detail));
                        return Result.Unauthorized(new ApiProblemDescriptor(
                            AuthProblemCodes.ExternalIdentityConflict,
                            detail));
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

                    if (!await userManager.HasPasswordAsync(user))
                    {
                        const string detail =
                            "AICopilot 本地账号没有可用于确认的密码，请联系 AI 管理员处理。";
                        rejectionAudit.Set(CreateRejectedAudit(
                            "Identity.CloudOidcExistingAccountHasNoPassword",
                            profile,
                            detail,
                            user));
                        return Result.Unauthorized(new ApiProblemDescriptor(
                            AuthProblemCodes.ExternalIdentityConflict,
                            detail));
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

                    var bindingResult = await ResolveBindingAsync(
                        user,
                        externalBinding,
                        profile,
                        ct);
                    if (!bindingResult.IsSuccess)
                    {
                        rejectionAudit.Set(CreateRejectedAudit(
                            bindingResult.AuditActionCode!,
                            profile,
                            bindingResult.Problem!.Detail,
                            user));
                        return Result.Unauthorized(bindingResult.Problem!);
                    }

                    if (string.IsNullOrWhiteSpace(user.SecurityStamp))
                    {
                        throw new InvalidOperationException(
                            $"Confirmed AICopilot user '{user.Id}' has no security stamp.");
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
                            bindingResult.WasCreated
                                ? "Cloud 身份已由本地密码确认并绑定到现有 AI 账号。"
                                : "Cloud 身份再次由本地密码确认，复用现有 AI 账号绑定。",
                            BuildChangedFields(profile, bindingResult.WasCreated),
                            BuildAuditMetadata(profile)),
                        ct);

                    return Result.Success(new LoginUserDto(user.UserName!, token));
                },
                cancellationToken);
        }
        catch (ExternalIdentityInvariantConflictException exception)
        {
            var problem = CreateKnownInvariantConflictProblem(exception.ConflictKind);
            rejectionAudit.Clear();
            rejectionAudit.Set(CreateRejectedAudit(
                ResolveKnownInvariantConflictAuditCode(exception.ConflictKind),
                profile,
                problem.Detail));
            result = Result.Unauthorized(problem);
        }

        if (!result.IsSuccess && rejectionAudit.Request is not null)
        {
            await transactionalExecutionService.CommitRejectedAuditAsync(
                auditLogWriter,
                rejectionAudit.Request,
                cancellationToken);
        }

        return result;
    }

    private async Task<ExternalIdentityBindingResolution> ResolveBindingAsync(
        ApplicationUser user,
        ExternalIdentityBindingSnapshot? externalBinding,
        CloudOidcIdentityProfile profile,
        CancellationToken cancellationToken)
    {
        var userBinding = await bindingStore.FindByUserProviderAsync(
            user.Id,
            ExternalIdentityProviders.Cloud,
            cancellationToken);

        if (externalBinding is not null && externalBinding.UserId != user.Id)
        {
            return ExternalIdentityBindingResolution.Conflict(
                "Identity.CloudOidcExternalIdentityBoundToDifferentUser",
                "该 Cloud 身份已绑定到另一个 AICopilot 本地账号，拒绝覆盖。");
        }

        if (userBinding is not null &&
            (!string.Equals(userBinding.TenantId, profile.TenantId, StringComparison.Ordinal) ||
             !string.Equals(userBinding.ExternalUserId, profile.Subject, StringComparison.Ordinal)))
        {
            return ExternalIdentityBindingResolution.Conflict(
                "Identity.CloudOidcLocalUserBoundToDifferentIdentity",
                "该 AICopilot 本地账号已绑定到另一个 Cloud 身份，拒绝覆盖。");
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
            return ExternalIdentityBindingResolution.Success(wasCreated: false);
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
        return ExternalIdentityBindingResolution.Success(wasCreated: true);
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

    private static ApiProblemDescriptor CreateKnownInvariantConflictProblem(
        ExternalIdentityInvariantConflictKind conflictKind)
    {
        var detail = conflictKind switch
        {
            ExternalIdentityInvariantConflictKind.NormalizedUserName =>
                "该 Cloud 身份对应的 AICopilot 用户名已被其他账号占用，请重新从 Cloud 登录。",
            ExternalIdentityInvariantConflictKind.ExternalIdentity =>
                "该 Cloud 身份已绑定到另一个 AICopilot 本地账号，拒绝覆盖。",
            ExternalIdentityInvariantConflictKind.UserProvider =>
                "该 AICopilot 本地账号已绑定到另一个 Cloud 身份，拒绝覆盖。",
            _ => throw new ArgumentOutOfRangeException(nameof(conflictKind), conflictKind, null)
        };
        return new ApiProblemDescriptor(AuthProblemCodes.ExternalIdentityConflict, detail);
    }

    private static string ResolveKnownInvariantConflictAuditCode(
        ExternalIdentityInvariantConflictKind conflictKind)
    {
        return conflictKind switch
        {
            ExternalIdentityInvariantConflictKind.NormalizedUserName =>
                "Identity.CloudOidcNormalizedUserNameConflict",
            ExternalIdentityInvariantConflictKind.ExternalIdentity =>
                "Identity.CloudOidcExternalIdentityBoundToDifferentUser",
            ExternalIdentityInvariantConflictKind.UserProvider =>
                "Identity.CloudOidcLocalUserBoundToDifferentIdentity",
            _ => throw new ArgumentOutOfRangeException(nameof(conflictKind), conflictKind, null)
        };
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

    private sealed record ExternalIdentityBindingResolution(
        bool IsSuccess,
        bool WasCreated,
        string? AuditActionCode,
        ApiProblemDescriptor? Problem)
    {
        public static ExternalIdentityBindingResolution Success(bool wasCreated)
        {
            return new ExternalIdentityBindingResolution(
                IsSuccess: true,
                wasCreated,
                AuditActionCode: null,
                Problem: null);
        }

        public static ExternalIdentityBindingResolution Conflict(
            string auditActionCode,
            string detail)
        {
            return new ExternalIdentityBindingResolution(
                IsSuccess: false,
                WasCreated: false,
                auditActionCode,
                new ApiProblemDescriptor(AuthProblemCodes.ExternalIdentityConflict, detail));
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
