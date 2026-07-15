using System.Security.Claims;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.Services.Contracts.Authentication;

public static class CloudOidcPrincipalMapper
{
    public static bool TryMap(
        ClaimsPrincipal principal,
        string issuer,
        out CloudOidcIdentityProfile profile,
        out ApiProblemDescriptor? problem)
    {
        var subject = FindClaim(principal, "sub", ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(subject))
        {
            profile = default!;
            problem = new ApiProblemDescriptor(
                AuthProblemCodes.CloudOidcInvalidPrincipal,
                "Cloud 登录响应缺少用户标识，无法完成 AI 登录。");
            return false;
        }

        if (!TryReadRequiredBoolean(principal, "account_enabled", out var accountEnabled))
        {
            profile = default!;
            problem = new ApiProblemDescriptor(
                AuthProblemCodes.CloudOidcInvalidPrincipal,
                "Cloud 登录响应缺少账号有效性声明。");
            return false;
        }

        if (!TryReadRequiredBoolean(principal, "employee_active", out var employeeActive))
        {
            profile = default!;
            problem = new ApiProblemDescriptor(
                AuthProblemCodes.CloudOidcInvalidPrincipal,
                "Cloud 登录响应缺少员工有效性声明。");
            return false;
        }

        var tenantId = NormalizeTenantId(FindClaim(principal, "tenant_id"));
        var preferredUserName = NormalizeRequired(
            FindClaim(principal, "preferred_username", ClaimTypes.Name) ?? subject);

        profile = new CloudOidcIdentityProfile(
            issuer,
            subject.Trim(),
            tenantId,
            preferredUserName,
            EmptyToNull(FindClaim(principal, "name")),
            EmptyToNull(FindClaim(principal, "employee_id")),
            EmptyToNull(FindClaim(principal, "employee_no")),
            EmptyToNull(FindClaim(principal, "department_id")),
            EmptyToNull(FindClaim(principal, "department_name")),
            EmptyToNull(FindClaim(principal, "status_version")),
            accountEnabled,
            employeeActive);
        problem = null;
        return true;
    }

    private static string? FindClaim(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = principal.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryReadRequiredBoolean(
        ClaimsPrincipal principal,
        string claimType,
        out bool value)
    {
        var rawValue = FindClaim(principal, claimType);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            value = false;
            return false;
        }

        if (bool.TryParse(rawValue, out value))
        {
            return true;
        }

        if (rawValue == "1")
        {
            value = true;
            return true;
        }

        if (rawValue == "0")
        {
            value = false;
            return true;
        }

        return false;
    }

    private static string NormalizeTenantId(string? tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId) ? CloudOidcIdentityProfile.DefaultTenantId : tenantId.Trim();
    }

    private static string NormalizeRequired(string value)
    {
        return value.Trim();
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
