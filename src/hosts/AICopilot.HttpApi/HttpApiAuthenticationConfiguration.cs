using AICopilot.HttpApi.Infrastructure;
using AICopilot.IdentityService.Authorization;
using AICopilot.Infrastructure.Authentication;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace AICopilot.HttpApi;

internal static class HttpApiAuthenticationConfiguration
{
    private const string AuthFailureCodeKey = "AuthFailureCode";
    private const string AuthFailureDetailKey = "AuthFailureDetail";
    private const string AuthFailureExtensionsKey = "AuthFailureExtensions";

    public static void AddHttpApiAuthentication(
        IServiceCollection services,
        JwtSettings jwtSettings,
        CloudOidcOptions cloudOidcOptions,
        CloudIdentityStatusOptions cloudIdentityStatusOptions)
    {
        var authenticationBuilder = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        });

        authenticationBuilder.AddCookie(
            CloudOidcAuthenticationDefaults.ExternalCookieScheme,
            options =>
            {
                options.Cookie.Name = cloudOidcOptions.GetEffectiveExternalCookieName();
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = cloudOidcOptions.AllowIntranetHttpOidc
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(
                    Math.Clamp(cloudOidcOptions.ExternalCookieLifetimeMinutes, 1, 15));
                options.SlidingExpiration = false;
            });

        if (cloudOidcOptions.IsConfigured())
        {
            var useHttpCompatibleRemoteCookies = cloudOidcOptions.UseHttpCompatibleRemoteCookies();
            var remoteCookieSecurePolicy = useHttpCompatibleRemoteCookies
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            var remoteCookieSameSite = useHttpCompatibleRemoteCookies
                ? SameSiteMode.Lax
                : SameSiteMode.None;

            authenticationBuilder.AddOpenIdConnect(
                CloudOidcAuthenticationDefaults.AuthenticationScheme,
                options =>
                {
                    options.Authority = cloudOidcOptions.Issuer.TrimEnd('/');
                    options.ClientId = cloudOidcOptions.ClientId;
                    options.ResponseType = OpenIdConnectResponseType.Code;
                    options.ResponseMode = OpenIdConnectResponseMode.Query;
                    options.UsePkce = true;
                    options.CallbackPath = cloudOidcOptions.CallbackPath;
                    options.SignInScheme = CloudOidcAuthenticationDefaults.ExternalCookieScheme;
                    options.RequireHttpsMetadata = cloudOidcOptions.GetEffectiveRequireHttpsMetadata();
                    options.SaveTokens = false;
                    options.GetClaimsFromUserInfoEndpoint = true;
                    options.MapInboundClaims = false;
                    options.CorrelationCookie.SecurePolicy = remoteCookieSecurePolicy;
                    options.CorrelationCookie.SameSite = remoteCookieSameSite;
                    options.NonceCookie.SecurePolicy = remoteCookieSecurePolicy;
                    options.NonceCookie.SameSite = remoteCookieSameSite;

                    options.Scope.Clear();
                    foreach (var scope in cloudOidcOptions.Scopes
                                 .Where(scope => !string.IsNullOrWhiteSpace(scope))
                                 .Select(scope => scope.Trim())
                                 .Distinct(StringComparer.Ordinal))
                    {
                        options.Scope.Add(scope);
                    }

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = cloudOidcOptions.Issuer.TrimEnd('/'),
                        ValidateAudience = true,
                        ValidAudience = cloudOidcOptions.ClientId,
                        NameClaimType = "preferred_username"
                    };

                    options.ClaimActions.MapUniqueJsonKey("sub", "sub");
                    options.ClaimActions.MapUniqueJsonKey("preferred_username", "preferred_username");
                    options.ClaimActions.MapUniqueJsonKey("name", "name");
                    options.ClaimActions.MapUniqueJsonKey("employee_no", "employee_no");
                    options.ClaimActions.MapUniqueJsonKey("employee_id", "employee_id");
                    options.ClaimActions.MapUniqueJsonKey("account_enabled", "account_enabled");
                    options.ClaimActions.MapUniqueJsonKey("employee_active", "employee_active");
                    options.ClaimActions.MapUniqueJsonKey("department_id", "department_id");
                    options.ClaimActions.MapUniqueJsonKey("department_name", "department_name");
                    options.ClaimActions.MapUniqueJsonKey("tenant_id", "tenant_id");
                    options.ClaimActions.MapUniqueJsonKey("status_version", "status_version");

                    options.Events.OnRemoteFailure = context =>
                    {
                        context.HandleResponse();
                        context.Response.Redirect(AppendQueryString(
                            cloudOidcOptions.FrontendCompletionPath,
                            "error",
                            "cloud_oidc_failed"));
                        return Task.CompletedTask;
                    };
                });
        }

        authenticationBuilder.AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
                ClockSkew = TimeSpan.Zero
            };
            options.Events = BuildJwtBearerEvents(cloudIdentityStatusOptions);
        });
    }

    private static JwtBearerEvents BuildJwtBearerEvents(CloudIdentityStatusOptions cloudIdentityStatusOptions)
    {
        return new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userManager = context.HttpContext.RequestServices
                    .GetRequiredService<UserManager<ApplicationUser>>();

                var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrWhiteSpace(userId))
                {
                    StoreAuthFailure(
                        context.HttpContext,
                        new ApiProblemDescriptor(
                            AuthProblemCodes.SessionRevoked,
                            "当前登录态无效，请重新登录。"));
                    context.Fail("Missing user id claim.");
                    return;
                }

                if (!Guid.TryParse(userId, out _))
                {
                    StoreAuthFailure(
                        context.HttpContext,
                        new ApiProblemDescriptor(
                            AuthProblemCodes.SessionRevoked,
                            "当前登录态无效，请重新登录。"));
                    context.Fail("Invalid user id claim.");
                    return;
                }

                var user = await userManager.FindByIdAsync(userId);
                if (user is null)
                {
                    StoreAuthFailure(
                        context.HttpContext,
                        new ApiProblemDescriptor(
                            AuthProblemCodes.UserMissing,
                            "当前用户不存在，请重新登录。"));
                    context.Fail("User was not found.");
                    return;
                }

                if (IdentityGovernanceHelper.IsUserDisabled(user))
                {
                    StoreAuthFailure(
                        context.HttpContext,
                        new ApiProblemDescriptor(
                            AuthProblemCodes.AccountDisabled,
                            "账号已禁用，请联系管理员恢复启用。"));
                    context.Fail("User account is disabled.");
                    return;
                }

                var tokenSecurityStamp = context.Principal?.FindFirstValue(JwtClaimTypes.SecurityStamp);
                var currentSecurityStamp = user.SecurityStamp ?? string.Empty;
                if (!string.Equals(tokenSecurityStamp ?? string.Empty, currentSecurityStamp, StringComparison.Ordinal))
                {
                    StoreAuthFailure(
                        context.HttpContext,
                        new ApiProblemDescriptor(
                            AuthProblemCodes.SessionRevoked,
                            "登录态已失效，请重新登录。"));
                    context.Fail("Security stamp mismatch.");
                    return;
                }

                var requiresCloudStatusCheck =
                    cloudIdentityStatusOptions.Enabled &&
                    string.Equals(
                        context.Principal?.FindFirstValue(ExternalIdentityJwtClaimTypes.IdentityProvider),
                        ExternalIdentityProviders.Cloud,
                        StringComparison.Ordinal);
                if (!requiresCloudStatusCheck)
                {
                    return;
                }

                var cloudIdentityStatusValidator = context.HttpContext.RequestServices
                    .GetRequiredService<ICloudIdentityStatusValidator>();
                var cloudIdentityStatus = await cloudIdentityStatusValidator.ValidateAsync(
                    user,
                    context.Principal!,
                    context.HttpContext.RequestAborted);
                if (!cloudIdentityStatus.IsValid)
                {
                    StoreAuthFailure(
                        context.HttpContext,
                        new ApiProblemDescriptor(
                            cloudIdentityStatus.FailureCode ?? AuthProblemCodes.SessionRevoked,
                            cloudIdentityStatus.FailureMessage ?? "登录态已失效，请重新登录。"));
                    context.Fail(cloudIdentityStatus.FailureMessage ?? "Cloud identity status check failed.");
                }
            },
            OnChallenge = async context =>
            {
                if (context.Response.HasStarted)
                {
                    return;
                }

                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";

                var problem = new ApiProblemDescriptor(
                    context.HttpContext.Items[AuthFailureCodeKey] as string ?? AuthProblemCodes.Unauthorized,
                    context.HttpContext.Items[AuthFailureDetailKey] as string
                        ?? context.ErrorDescription
                        ?? "当前请求未通过身份验证。",
                    context.HttpContext.Items[AuthFailureExtensionsKey] as IReadOnlyDictionary<string, object?>);

                await context.Response.WriteAsJsonAsync(
                    ApiProblemDetailsFactory.Create(
                        StatusCodes.Status401Unauthorized,
                        problem,
                        traceIdentifier: context.HttpContext.TraceIdentifier),
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken: context.HttpContext.RequestAborted);
            }
        };
    }

    private static void StoreAuthFailure(HttpContext httpContext, ApiProblemDescriptor problem)
    {
        httpContext.Items[AuthFailureCodeKey] = problem.Code;
        httpContext.Items[AuthFailureDetailKey] = problem.Detail;
        if (problem.Extensions is not null)
        {
            httpContext.Items[AuthFailureExtensionsKey] = problem.Extensions;
        }
    }

    private static string AppendQueryString(string path, string key, string value)
    {
        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{path}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
    }
}
