using System.Security.Claims;
using AICopilot.IdentityService.Authorization;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;

namespace AICopilot.IdentityService.Commands;

public record FinalizeCloudOidcLoginCommand(CloudOidcIdentityProfile Profile)
    : ICommand<Result<LoginUserDto>>;

public sealed class FinalizeCloudOidcLoginCommandHandler(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IExternalIdentityBindingStore bindingStore,
    IIdentityAuditLogWriter auditLogWriter,
    IJwtTokenGenerator jwtTokenGenerator,
    ITransactionalExecutionService transactionalExecutionService)
    : ICommandHandler<FinalizeCloudOidcLoginCommand, Result<LoginUserDto>>
{
    public async Task<Result<LoginUserDto>> Handle(
        FinalizeCloudOidcLoginCommand command,
        CancellationToken cancellationToken)
    {
        var profile = NormalizeProfile(command.Profile);

        return await transactionalExecutionService.ExecuteAsync(
            async ct =>
            {
                if (!profile.AccountEnabled)
                {
                    await WriteRejectedAuditAsync(
                        "Identity.CloudOidcAccountDisabled",
                        profile,
                        "Cloud 账号已禁用，拒绝换取 AI 登录态。",
                        ct);

                    return Result.Unauthorized(new ApiProblemDescriptor(
                        AuthProblemCodes.CloudIdentityInactive,
                        "Cloud 账号已禁用，无法登录 AICopilot。"));
                }

                if (!profile.EmployeeActive)
                {
                    await WriteRejectedAuditAsync(
                        "Identity.CloudOidcEmployeeInactive",
                        profile,
                        "Cloud 员工已失效，拒绝换取 AI 登录态。",
                        ct);

                    return Result.Unauthorized(new ApiProblemDescriptor(
                        AuthProblemCodes.CloudIdentityInactive,
                        "Cloud 员工状态无效，无法登录 AICopilot。"));
                }

                return await FinalizeLoginAsync(profile, ct);
            },
            cancellationToken);
    }

    private async Task<Result<LoginUserDto>> FinalizeLoginAsync(
        CloudOidcIdentityProfile profile,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var binding = await bindingStore.FindByExternalIdentityAsync(
            ExternalIdentityProviders.Cloud,
            profile.TenantId,
            profile.Subject,
            cancellationToken);

        var isFirstBinding = binding is null;
        var user = binding is null
            ? await CreateBoundUserAsync(profile, now, cancellationToken)
            : await LoadBoundUserAsync(profile, binding, now, cancellationToken);

        if (user is null)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.ExternalIdentityConflict,
                "Cloud 身份与现有 AI 账号存在冲突，已拒绝自动绑定。"));
        }

        if (IdentityGovernanceHelper.IsUserDisabled(user))
        {
            await WriteRejectedAuditAsync(
                "Identity.CloudOidcLocalUserDisabled",
                profile,
                $"AI 本地用户 {user.UserName} 已禁用，拒绝 Cloud 登录。",
                cancellationToken,
                user.Id.ToString(),
                user.UserName);

            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.AccountDisabled,
                "AICopilot 本地账号已禁用，请联系 AI 管理员恢复启用。"));
        }

        if (string.IsNullOrWhiteSpace(user.SecurityStamp))
        {
            await userManager.UpdateSecurityStampAsync(user);
            user = await userManager.FindByIdAsync(user.Id.ToString())
                ?? throw new InvalidOperationException($"User '{user.Id}' was not found after updating security stamp.");
        }

        var token = await GenerateAiTokenAsync(user, profile, cancellationToken);

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Identity,
                isFirstBinding ? "Identity.CloudOidcFirstBind" : "Identity.CloudOidcLogin",
                "ExternalIdentityBinding",
                user.Id.ToString(),
                user.UserName ?? profile.PreferredUserName,
                AuditResults.Succeeded,
                isFirstBinding
                    ? $"Cloud 身份首次绑定 AI 用户：{profile.Subject} -> {user.UserName}"
                    : $"Cloud 身份复用已绑定 AI 用户：{profile.Subject} -> {user.UserName}",
                BuildChangedFields(profile, includeBindingFields: isFirstBinding),
                BuildAuditMetadata(profile)),
            cancellationToken);

        return Result.Success(new LoginUserDto(user.UserName!, token));
    }

    private async Task<ApplicationUser?> CreateBoundUserAsync(
        CloudOidcIdentityProfile profile,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var localUserName = ResolveLocalUserName(profile);
        var existingUser = await userManager.FindByNameAsync(localUserName);
        if (existingUser is not null)
        {
            await WriteRejectedAuditAsync(
                "Identity.CloudOidcBindingConflict",
                profile,
                $"Cloud 身份 {profile.Subject} 的本地用户名 {localUserName} 已存在，拒绝自动绑定。",
                cancellationToken,
                existingUser.Id.ToString(),
                existingUser.UserName);
            return null;
        }

        if (!await roleManager.RoleExistsAsync(IdentityRoleNames.User))
        {
            await WriteRejectedAuditAsync(
                "Identity.CloudOidcMissingDefaultRole",
                profile,
                "AI 默认 User 角色不存在，拒绝 Cloud 登录。",
                cancellationToken);
            return null;
        }

        var user = new ApplicationUser
        {
            UserName = localUserName,
            SecurityStamp = Guid.NewGuid().ToString("N")
        };

        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            await WriteRejectedAuditAsync(
                "Identity.CloudOidcCreateUserFailed",
                profile,
                "Cloud 身份 JIT 创建 AI 用户失败。",
                cancellationToken);
            return null;
        }

        var roleResult = await userManager.AddToRoleAsync(user, IdentityRoleNames.User);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            await WriteRejectedAuditAsync(
                "Identity.CloudOidcAssignDefaultRoleFailed",
                profile,
                "Cloud 身份 JIT 创建后分配默认 User 角色失败。",
                cancellationToken,
                user.Id.ToString(),
                user.UserName);
            return null;
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
                now),
            cancellationToken);

        return user;
    }

    private async Task<ApplicationUser?> LoadBoundUserAsync(
        CloudOidcIdentityProfile profile,
        ExternalIdentityBindingSnapshot binding,
        DateTime now,
        CancellationToken cancellationToken)
    {
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

        var user = await userManager.FindByIdAsync(binding.UserId.ToString());
        if (user is not null)
        {
            return user;
        }

        await WriteRejectedAuditAsync(
            "Identity.CloudOidcBoundUserMissing",
            profile,
            $"Cloud 身份绑定的 AI 用户 {binding.UserId} 不存在，拒绝登录。",
            cancellationToken,
            binding.UserId.ToString(),
            profile.PreferredUserName);
        return null;
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

    private async Task WriteRejectedAuditAsync(
        string actionCode,
        CloudOidcIdentityProfile profile,
        string summary,
        CancellationToken cancellationToken,
        string? targetId = null,
        string? targetName = null)
    {
        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                AuditActionGroups.Identity,
                actionCode,
                "ExternalIdentityBinding",
                targetId ?? $"{profile.TenantId}:{profile.Subject}",
                targetName ?? profile.PreferredUserName,
                AuditResults.Rejected,
                summary,
                BuildChangedFields(profile, includeBindingFields: false),
                BuildAuditMetadata(profile, actionCode)),
            cancellationToken);
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
}
