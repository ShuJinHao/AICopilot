using System.Security.Claims;
using AICopilot.IdentityService.Authorization;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AICopilot.IdentityService.Commands;

public record FinalizeCloudOidcLoginCommand(CloudOidcIdentityProfile Profile)
    : ICommand<Result<LoginUserDto>>;

public sealed class FinalizeCloudOidcLoginCommandHandler(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IExternalIdentityBindingStore bindingStore,
    IIdentityUserFreshReadStore userFreshReadStore,
    IExternalIdentityBindingInvariantGuard bindingInvariantGuard,
    IIdentityAuditLogWriter auditLogWriter,
    IJwtTokenGenerator jwtTokenGenerator,
    IOptions<CloudOidcBootstrapAdminBindingOptions> bootstrapAdminBindingOptions,
    ITransactionalExecutionService transactionalExecutionService)
    : ICommandHandler<FinalizeCloudOidcLoginCommand, Result<LoginUserDto>>
{
    public async Task<Result<LoginUserDto>> Handle(
        FinalizeCloudOidcLoginCommand command,
        CancellationToken cancellationToken)
    {
        var profile = NormalizeProfile(command.Profile);
        var rejectionAudit = new RejectionAuditBuffer();
        Result<LoginUserDto> result;
        try
        {
            result = await transactionalExecutionService.ExecuteResultAsync(
                async ct =>
                {
                    rejectionAudit.Clear();
                    if (!profile.AccountEnabled)
                    {
                        rejectionAudit.Set(CreateRejectedAudit(
                            "Identity.CloudOidcAccountDisabled",
                            profile,
                            "Cloud 账号已禁用，拒绝换取 AI 登录态。"));

                        return Result.Unauthorized(new ApiProblemDescriptor(
                            AuthProblemCodes.CloudIdentityInactive,
                            "Cloud 账号已禁用，无法登录 AICopilot。"));
                    }

                    if (!profile.EmployeeActive)
                    {
                        rejectionAudit.Set(CreateRejectedAudit(
                            "Identity.CloudOidcEmployeeInactive",
                            profile,
                            "Cloud 员工已失效，拒绝换取 AI 登录态。"));

                        return Result.Unauthorized(new ApiProblemDescriptor(
                            AuthProblemCodes.CloudIdentityInactive,
                            "Cloud 员工状态无效，无法登录 AICopilot。"));
                    }

                    return await FinalizeLoginAsync(profile, rejectionAudit, ct);
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

    private async Task<Result<LoginUserDto>> FinalizeLoginAsync(
        CloudOidcIdentityProfile profile,
        RejectionAuditBuffer rejectionAudit,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var localUserName = ResolveLocalUserName(profile);
        var normalizedUserName = userManager.NormalizeName(localUserName);
        if (string.IsNullOrWhiteSpace(normalizedUserName))
        {
            throw new InvalidOperationException(
                "Cloud OIDC profile did not produce a normalized AICopilot user name.");
        }

        var bindingBeforeLock = await bindingStore.FindByExternalIdentityAsync(
            ExternalIdentityProviders.Cloud,
            profile.TenantId,
            profile.Subject,
            cancellationToken);
        var userBeforeLock = await userFreshReadStore.FindByNormalizedUserNameAsync(
            normalizedUserName,
            cancellationToken);
        var prospectiveUserId = Guid.NewGuid();
        var knownUserIds = new[]
            {
                bindingBeforeLock?.UserId,
                userBeforeLock?.Id,
                prospectiveUserId
            }
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
            cancellationToken);

        var binding = await bindingStore.FindByExternalIdentityAsync(
            ExternalIdentityProviders.Cloud,
            profile.TenantId,
            profile.Subject,
            cancellationToken);
        var localUser = await userFreshReadStore.FindByNormalizedUserNameAsync(
            normalizedUserName,
            cancellationToken);
        if (binding is not null && localUser is not null && localUser.Id != binding.UserId)
        {
            const string detail =
                "Cloud profile 对应的 AICopilot 用户名属于另一个本地账号，与既有绑定不一致。";
            rejectionAudit.Set(CreateRejectedAudit(
                "Identity.CloudOidcProfileUserNameConflict",
                profile,
                detail,
                localUser.Id.ToString(),
                localUser.UserName));
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.ExternalIdentityConflict,
                detail));
        }

        var resolution = binding is null
            ? await ResolveFirstBindingUserAsync(
                profile,
                now,
                localUserName,
                localUser,
                prospectiveUserId,
                rejectionAudit,
                cancellationToken)
            : new CloudOidcLoginResolution(
                await LoadBoundUserAsync(profile, binding, now, cancellationToken),
                IsFirstBinding: false,
                IsBootstrapAdminAdoption: false,
                RejectionProblem: null);
        var user = resolution.User;

        if (user is null)
        {
            return Result.Unauthorized(
                resolution.RejectionProblem ??
                throw new InvalidOperationException(
                    "A rejected Cloud OIDC login resolution did not provide a precise problem."));
        }

        if (IdentityGovernanceHelper.IsUserDisabled(user))
        {
            rejectionAudit.Set(CreateRejectedAudit(
                "Identity.CloudOidcLocalUserDisabled",
                profile,
                $"AI 本地用户 {user.UserName} 已禁用，拒绝 Cloud 登录。",
                user.Id.ToString(),
                user.UserName));

            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.AccountDisabled,
                "AICopilot 本地账号已禁用，请联系 AI 管理员恢复启用。"));
        }

        user = await EnsureSecurityStampAsync(user, cancellationToken);

        var token = await GenerateAiTokenAsync(user, profile, cancellationToken);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Identity,
                ResolveLoginAuditActionCode(resolution),
                "ExternalIdentityBinding",
                user.Id.ToString(),
                user.UserName ?? profile.PreferredUserName,
                AuditResults.Succeeded,
                ResolveLoginAuditSummary(profile, user, resolution),
                BuildChangedFields(profile, includeBindingFields: resolution.IsFirstBinding),
                BuildAuditMetadata(profile)),
            cancellationToken);

        return Result.Success(new LoginUserDto(user.UserName!, token));
    }

    private async Task<ApplicationUser> EnsureSecurityStampAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(user.SecurityStamp))
        {
            return user;
        }

        var refreshedUser = await userFreshReadStore.InitializeSecurityStampIfMissingAsync(
            user.Id,
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString(),
            cancellationToken);
        if (refreshedUser is null || string.IsNullOrWhiteSpace(refreshedUser.SecurityStamp))
        {
            throw new InvalidOperationException(
                $"Cloud-bound AICopilot user '{user.Id}' was not found with a persisted security stamp.");
        }

        return refreshedUser;
    }

    private async Task<CloudOidcLoginResolution> ResolveFirstBindingUserAsync(
        CloudOidcIdentityProfile profile,
        DateTime now,
        string localUserName,
        ApplicationUser? existingUser,
        Guid prospectiveUserId,
        RejectionAuditBuffer rejectionAudit,
        CancellationToken cancellationToken)
    {
        if (existingUser is not null)
        {
            if (IdentityGovernanceHelper.IsUserDisabled(existingUser))
            {
                rejectionAudit.Set(CreateRejectedAudit(
                    "Identity.CloudOidcLocalUserDisabled",
                    profile,
                    $"AI 本地用户 {existingUser.UserName} 已禁用，拒绝 Cloud 登录。",
                    existingUser.Id.ToString(),
                    existingUser.UserName));
                return CloudOidcLoginResolution.Rejected(
                    new ApiProblemDescriptor(
                        AuthProblemCodes.AccountDisabled,
                        "AICopilot 本地账号已禁用，请联系 AI 管理员恢复启用。"));
            }

            var existingUserBinding = await bindingStore.FindByUserProviderAsync(
                existingUser.Id,
                ExternalIdentityProviders.Cloud,
                cancellationToken);
            if (existingUserBinding is not null)
            {
                if (string.Equals(existingUserBinding.TenantId, profile.TenantId, StringComparison.Ordinal) &&
                    string.Equals(existingUserBinding.ExternalUserId, profile.Subject, StringComparison.Ordinal))
                {
                    await bindingStore.UpdateSnapshotAsync(
                        new UpdateExternalIdentityBindingSnapshotRequest(
                            existingUserBinding.Id,
                            profile.EmployeeId,
                            profile.EmployeeNo,
                            profile.DisplayName,
                            profile.DepartmentId,
                            profile.DepartmentName,
                            profile.StatusVersion,
                            profile.AccountEnabled,
                            profile.EmployeeActive,
                            now),
                        cancellationToken);
                    return new CloudOidcLoginResolution(
                        existingUser,
                        IsFirstBinding: false,
                        IsBootstrapAdminAdoption: false,
                        RejectionProblem: null);
                }

                rejectionAudit.Set(CreateRejectedAudit(
                    "Identity.CloudOidcLocalUserBoundToDifferentIdentity",
                    profile,
                    $"AI 本地用户 {existingUser.UserName} 已绑定其他 Cloud 身份，拒绝覆盖。",
                    existingUser.Id.ToString(),
                    existingUser.UserName));
                return CloudOidcLoginResolution.Rejected(
                    new ApiProblemDescriptor(
                        AuthProblemCodes.ExternalIdentityConflict,
                        "该 AICopilot 本地账号已绑定到另一个 Cloud 身份，拒绝覆盖。"));
            }

            var adoptedUser = await TryAdoptBootstrapAdminAsync(
                existingUser,
                profile,
                now,
                cancellationToken);
            if (adoptedUser is not null)
            {
                return new CloudOidcLoginResolution(
                    adoptedUser,
                    IsFirstBinding: true,
                    IsBootstrapAdminAdoption: true,
                    RejectionProblem: null);
            }

            if (!await userManager.HasPasswordAsync(existingUser))
            {
                rejectionAudit.Set(CreateRejectedAudit(
                    "Identity.CloudOidcExistingAccountHasNoPassword",
                    profile,
                    $"AI 本地用户 {localUserName} 没有可用于确认的本地密码，拒绝绑定。",
                    existingUser.Id.ToString(),
                    existingUser.UserName));
                return CloudOidcLoginResolution.Rejected(
                    new ApiProblemDescriptor(
                        AuthProblemCodes.ExternalIdentityConflict,
                        "AICopilot 本地账号没有可用于确认的密码，请联系 AI 管理员处理。"));
            }

            rejectionAudit.Set(CreateRejectedAudit(
                "Identity.CloudOidcExistingAccountConfirmationRequired",
                profile,
                $"Cloud 身份 {profile.Subject} 的本地用户名 {localUserName} 已存在，需要本地密码确认。",
                existingUser.Id.ToString(),
                existingUser.UserName));
            return CloudOidcLoginResolution.Rejected(
                new ApiProblemDescriptor(
                    AuthProblemCodes.ExternalIdentityConfirmationRequired,
                    "检测到同名的 AICopilot 本地账号，请输入该账号的本地密码完成绑定。"));
        }

        if (!await roleManager.RoleExistsAsync(IdentityRoleNames.User))
        {
            throw new InvalidOperationException(
                "AICopilot JIT login cannot create a user because the local User role is missing.");
        }

        var user = new ApplicationUser
        {
            Id = prospectiveUserId,
            UserName = localUserName,
            SecurityStamp = Guid.NewGuid().ToString("N")
        };

        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            if (createResult.Errors.Any(error =>
                    string.Equals(
                        error.Code,
                        nameof(IdentityErrorDescriber.DuplicateUserName),
                        StringComparison.Ordinal)))
            {
                const string detail =
                    "该 Cloud 身份对应的 AICopilot 用户名已被其他账号占用，请重新从 Cloud 登录。";
                rejectionAudit.Set(CreateRejectedAudit(
                    "Identity.CloudOidcNormalizedUserNameConflict",
                    profile,
                    detail));
                return CloudOidcLoginResolution.Rejected(
                    new ApiProblemDescriptor(
                        AuthProblemCodes.ExternalIdentityConflict,
                        detail));
            }

            throw new InvalidOperationException(
                $"Cloud OIDC JIT user creation failed: {string.Join(',', createResult.Errors.Select(error => error.Code))}");
        }

        var roleResult = await userManager.AddToRoleAsync(user, IdentityRoleNames.User);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Cloud OIDC JIT default-role assignment failed: {string.Join(',', roleResult.Errors.Select(error => error.Code))}");
        }

        await CreateBindingAsync(user.Id, profile, now, cancellationToken);

        return new CloudOidcLoginResolution(
            user,
            IsFirstBinding: true,
            IsBootstrapAdminAdoption: false,
            RejectionProblem: null);
    }

    private async Task<ApplicationUser?> TryAdoptBootstrapAdminAsync(
        ApplicationUser existingUser,
        CloudOidcIdentityProfile profile,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var options = bootstrapAdminBindingOptions.Value;
        if (!options.BootstrapAdminAutoBindEnabled ||
            string.IsNullOrWhiteSpace(options.BootstrapAdminUserName) ||
            string.IsNullOrWhiteSpace(profile.EmployeeNo) ||
            string.IsNullOrWhiteSpace(existingUser.UserName))
        {
            return null;
        }

        var bootstrapAdminUserName = options.BootstrapAdminUserName.Trim();
        if (!string.Equals(profile.EmployeeNo, bootstrapAdminUserName, StringComparison.Ordinal) ||
            !string.Equals(existingUser.UserName, bootstrapAdminUserName, StringComparison.Ordinal))
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(existingUser);
        if (!roles.Contains(IdentityRoleNames.Admin, StringComparer.Ordinal))
        {
            return null;
        }

        var existingUserBinding = await bindingStore.FindByUserProviderAsync(
            existingUser.Id,
            ExternalIdentityProviders.Cloud,
            cancellationToken);
        if (existingUserBinding is not null)
        {
            return null;
        }

        await CreateBindingAsync(existingUser.Id, profile, now, cancellationToken);
        return existingUser;
    }

    private Task CreateBindingAsync(
        Guid userId,
        CloudOidcIdentityProfile profile,
        DateTime now,
        CancellationToken cancellationToken)
    {
        return bindingStore.CreateAsync(
            new CreateExternalIdentityBindingRequest(
                userId,
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
                now),
            cancellationToken);
    }

    private async Task<ApplicationUser> LoadBoundUserAsync(
        CloudOidcIdentityProfile profile,
        ExternalIdentityBindingSnapshot binding,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var user = await userFreshReadStore.FindByIdAsync(
            binding.UserId,
            cancellationToken);
        if (user is null)
        {
            throw new InvalidOperationException(
                $"Cloud identity binding '{binding.Id}' references missing AICopilot user '{binding.UserId}'.");
        }

        await bindingStore.UpdateSnapshotAsync(
            new UpdateExternalIdentityBindingSnapshotRequest(
                binding.Id,
                profile.EmployeeId,
                profile.EmployeeNo,
                profile.DisplayName,
                profile.DepartmentId,
                profile.DepartmentName,
                profile.StatusVersion,
                profile.AccountEnabled,
                profile.EmployeeActive,
                now),
            cancellationToken);
        return user;
    }

    private async Task<string> GenerateAiTokenAsync(
        ApplicationUser user,
        CloudOidcIdentityProfile profile,
        CancellationToken cancellationToken)
    {
        var userClaims = await userManager.GetClaimsAsync(user);
        var userRoles = await userManager.GetRolesAsync(user);
        var cloudClaims = BuildCloudJwtClaims(profile);

        return await jwtTokenGenerator.GenerateTokenAsync(
            new JwtTokenUser(
                user.Id,
                user.UserName!,
                user.SecurityStamp ?? string.Empty,
                userRoles.ToArray(),
                userClaims.Concat(cloudClaims).ToArray()),
            cancellationToken);
    }

    private static string ResolveLoginAuditActionCode(CloudOidcLoginResolution resolution)
    {
        if (resolution.IsBootstrapAdminAdoption)
        {
            return "Identity.CloudOidcBootstrapAdminAdopted";
        }

        return resolution.IsFirstBinding ? "Identity.CloudOidcFirstBind" : "Identity.CloudOidcLogin";
    }

    private static string ResolveLoginAuditSummary(
        CloudOidcIdentityProfile profile,
        ApplicationUser user,
        CloudOidcLoginResolution resolution)
    {
        if (resolution.IsBootstrapAdminAdoption)
        {
            return $"Cloud 身份收编首部署 AI 管理员：{profile.Subject} -> {user.UserName}";
        }

        return resolution.IsFirstBinding
            ? $"Cloud 身份首次绑定 AI 用户：{profile.Subject} -> {user.UserName}"
            : $"Cloud 身份复用已绑定 AI 用户：{profile.Subject} -> {user.UserName}";
    }

    private static AuditLogWriteRequest CreateRejectedAudit(
        string actionCode,
        CloudOidcIdentityProfile profile,
        string summary,
        string? targetId = null,
        string? targetName = null)
    {
        return new AuditLogWriteRequest(
            AuditActionGroups.Identity,
            actionCode,
            "ExternalIdentityBinding",
            targetId ?? $"{profile.TenantId}:{profile.Subject}",
            targetName ?? profile.PreferredUserName,
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

        AddIfPresent(claims, ExternalIdentityJwtClaimTypes.CloudEmployeeId, profile.EmployeeId);
        AddIfPresent(claims, ExternalIdentityJwtClaimTypes.CloudEmployeeNo, profile.EmployeeNo);
        AddIfPresent(claims, ExternalIdentityJwtClaimTypes.CloudDepartmentId, profile.DepartmentId);
        AddIfPresent(claims, ExternalIdentityJwtClaimTypes.CloudDepartmentName, profile.DepartmentName);
        AddIfPresent(claims, ExternalIdentityJwtClaimTypes.CloudStatusVersion, profile.StatusVersion);

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
            fields.Add("roleName");
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
            ["authMethod"] = "CloudOidc"
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

    private static void AddIfPresent(List<Claim> claims, string claimType, string? value)
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

    private static void AddMetadataIfPresent(Dictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value.Trim();
        }
    }

    private sealed record CloudOidcLoginResolution(
        ApplicationUser? User,
        bool IsFirstBinding,
        bool IsBootstrapAdminAdoption,
        ApiProblemDescriptor? RejectionProblem)
    {
        public static CloudOidcLoginResolution Rejected(ApiProblemDescriptor problem)
        {
            return new CloudOidcLoginResolution(
                User: null,
                IsFirstBinding: true,
                IsBootstrapAdminAdoption: false,
                RejectionProblem: problem);
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
                    "Cloud OIDC login produced more than one rejection audit in a single attempt.");
            }

            Request = request;
        }
    }
}
