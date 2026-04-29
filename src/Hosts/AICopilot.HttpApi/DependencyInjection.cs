using AICopilot.AiGatewayService;
using AICopilot.DataAnalysisService;
using AICopilot.HttpApi.Infrastructure;
using AICopilot.IdentityService;
using AICopilot.IdentityService.Authorization;
using AICopilot.Infrastructure.Authentication;
using AICopilot.McpService;
using AICopilot.RagService;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace AICopilot.HttpApi;

public static class DependencyInjection
{
    extension(IHostApplicationBuilder builder)
    {
        public void AddApplicationService()
        {
            builder.AddIdentityService();
            builder.AddAiGatewayService();
            builder.AddRagService();
            builder.AddDataAnalysisService();
            builder.AddMcpService();
        }

        public void AddWebServices()
        {
            const string authFailureCodeKey = "AuthFailureCode";
            const string authFailureDetailKey = "AuthFailureDetail";
            const string authFailureExtensionsKey = "AuthFailureExtensions";

            var configurationSection = builder.Configuration.GetSection("JwtSettings");
            var jwtSettings = configurationSection.Get<JwtSettings>();
            if (jwtSettings is null)
            {
                throw new NullReferenceException(nameof(jwtSettings));
            }

            if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
            {
                throw new InvalidOperationException("JwtSettings:SecretKey is required; configure it with user-secrets or the JwtSettings__SecretKey environment variable.");
            }

            builder.Services.Configure<JwtSettings>(configurationSection);

            builder.Services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(options =>
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
                    options.Events = new JwtBearerEvents
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
                                context.HttpContext.Items[authFailureCodeKey] as string ?? AuthProblemCodes.Unauthorized,
                                context.HttpContext.Items[authFailureDetailKey] as string
                                    ?? context.ErrorDescription
                                    ?? "当前请求未通过身份验证。",
                                context.HttpContext.Items[authFailureExtensionsKey] as IReadOnlyDictionary<string, object?>);

                            await context.Response.WriteAsJsonAsync(
                                ApiProblemDetailsFactory.Create(StatusCodes.Status401Unauthorized, problem));
                        }
                    };
                });

            builder.Services.AddScoped<ICurrentUser, CurrentUser>();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddRateLimiter(options =>
            {
                var defaultTokenLimit = builder.Configuration.GetValue("RateLimiting:Default:TokenLimit", 60);
                var defaultTokensPerPeriod = builder.Configuration.GetValue("RateLimiting:Default:TokensPerPeriod", 60);
                var loginTokenLimit = builder.Configuration.GetValue("RateLimiting:Login:TokenLimit", 5);
                var loginTokensPerPeriod = builder.Configuration.GetValue("RateLimiting:Login:TokensPerPeriod", 5);
                var chatTokenLimit = builder.Configuration.GetValue("RateLimiting:Chat:TokenLimit", 12);
                var chatTokensPerPeriod = builder.Configuration.GetValue("RateLimiting:Chat:TokensPerPeriod", 12);
                var identityManagementTokenLimit = builder.Configuration.GetValue("RateLimiting:IdentityManagement:TokenLimit", 10);
                var identityManagementTokensPerPeriod = builder.Configuration.GetValue("RateLimiting:IdentityManagement:TokensPerPeriod", 10);

                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                    RateLimitPartition.GetTokenBucketLimiter(
                        GetDefaultPolicyPartitionKey(httpContext),
                        _ => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = defaultTokenLimit,
                            TokensPerPeriod = defaultTokensPerPeriod,
                            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                            AutoReplenishment = true,
                            QueueLimit = 0
                        }));

                options.AddPolicy("login", httpContext =>
                    RateLimitPartition.GetTokenBucketLimiter(
                        GetLoginPolicyPartitionKey(httpContext),
                        _ => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = loginTokenLimit,
                            TokensPerPeriod = loginTokensPerPeriod,
                            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                            AutoReplenishment = true,
                            QueueLimit = 0
                        }));

                options.AddPolicy("identity-management", httpContext =>
                    RateLimitPartition.GetTokenBucketLimiter(
                        GetIdentityManagementPolicyPartitionKey(httpContext),
                        _ => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = identityManagementTokenLimit,
                            TokensPerPeriod = identityManagementTokensPerPeriod,
                            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                            AutoReplenishment = true,
                            QueueLimit = 0
                        }));

                options.AddPolicy("chat", httpContext =>
                    RateLimitPartition.GetTokenBucketLimiter(
                        GetChatPolicyPartitionKey(httpContext),
                        _ => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = chatTokenLimit,
                            TokensPerPeriod = chatTokensPerPeriod,
                            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                            AutoReplenishment = true,
                            QueueLimit = 0
                        }));

                options.OnRejected = async (context, cancellationToken) =>
                {
                    var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue)
                        ? Math.Max(1, (int)Math.Ceiling(retryAfterValue.TotalSeconds))
                        : (int?)null;

                    var extensions = retryAfter.HasValue
                        ? new Dictionary<string, object?>
                        {
                            [ApiProblemExtensionKeys.RetryAfterSeconds] = retryAfter.Value
                        }
                        : null;

                    if (retryAfter.HasValue)
                    {
                        context.HttpContext.Response.Headers.RetryAfter = retryAfter.Value.ToString();
                    }

                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.HttpContext.Response.ContentType = "application/problem+json";

                    await context.HttpContext.Response.WriteAsJsonAsync(
                        ApiProblemDetailsFactory.Create(
                            StatusCodes.Status429TooManyRequests,
                            new ApiProblemDescriptor(
                                AppProblemCodes.RateLimitExceeded,
                                "请求过于频繁，请稍后再试。",
                                extensions)),
                        cancellationToken);
                };
            });
            builder.Services.AddExceptionHandler<UseCaseExceptionHandler>();
            builder.Services.AddProblemDetails();

            void StoreAuthFailure(HttpContext httpContext, ApiProblemDescriptor problem)
            {
                httpContext.Items[authFailureCodeKey] = problem.Code;
                httpContext.Items[authFailureDetailKey] = problem.Detail;
                if (problem.Extensions is not null)
                {
                    httpContext.Items[authFailureExtensionsKey] = problem.Extensions;
                }
            }

            static string GetDefaultPolicyPartitionKey(HttpContext httpContext)
            {
                var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
                var clientKey = string.IsNullOrWhiteSpace(userId)
                    ? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous"
                    : userId;

                return $"{httpContext.Request.Method}:{httpContext.Request.Path}:{clientKey}";
            }

            static string GetChatPolicyPartitionKey(HttpContext httpContext)
            {
                var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
                return $"chat:{(string.IsNullOrWhiteSpace(userId) ? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous" : userId)}";
            }

            static string GetLoginPolicyPartitionKey(HttpContext httpContext)
            {
                var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                var username = TryReadLoginUsername(httpContext);
                var normalizedUsername = string.IsNullOrWhiteSpace(username)
                    ? "anonymous"
                    : username.Trim().ToUpperInvariant();

                return $"login:{normalizedUsername}:{ipAddress}";
            }

            static string GetIdentityManagementPolicyPartitionKey(HttpContext httpContext)
            {
                var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
                var clientKey = string.IsNullOrWhiteSpace(userId)
                    ? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous"
                    : userId;

                return $"identity-management:{httpContext.Request.Method}:{httpContext.Request.Path}:{clientKey}";
            }

            static string? TryReadLoginUsername(HttpContext httpContext)
            {
                if (httpContext.Request.Headers.TryGetValue("X-Login-Username", out var headerUsername) &&
                    !string.IsNullOrWhiteSpace(headerUsername))
                {
                    return headerUsername.ToString();
                }

                if (httpContext.Request.Query.TryGetValue("username", out var queryUsername) &&
                    !string.IsNullOrWhiteSpace(queryUsername))
                {
                    return queryUsername.ToString();
                }

                if (!httpContext.Request.HasJsonContentType())
                {
                    return null;
                }

                if (httpContext.Request.ContentLength is > 8192)
                {
                    return null;
                }

                try
                {
                    httpContext.Request.EnableBuffering(bufferThreshold: 4096, bufferLimit: 8192);
                    if (!httpContext.Request.Body.CanSeek)
                    {
                        return null;
                    }

                    var originalPosition = httpContext.Request.Body.Position;
                    httpContext.Request.Body.Position = 0;

                    var bodyControlFeature = httpContext.Features.Get<IHttpBodyControlFeature>();
                    var previousSynchronousIoSetting = bodyControlFeature?.AllowSynchronousIO;
                    if (bodyControlFeature is not null)
                    {
                        bodyControlFeature.AllowSynchronousIO = true;
                    }

                    try
                    {
                        using var document = JsonDocument.Parse(httpContext.Request.Body);
                        if (document.RootElement.ValueKind != JsonValueKind.Object)
                        {
                            return null;
                        }

                        foreach (var property in document.RootElement.EnumerateObject())
                        {
                            if (string.Equals(property.Name, "username", StringComparison.OrdinalIgnoreCase) &&
                                property.Value.ValueKind == JsonValueKind.String)
                            {
                                return property.Value.GetString();
                            }
                        }

                        return null;
                    }
                    finally
                    {
                        httpContext.Request.Body.Position = originalPosition;
                        if (bodyControlFeature is not null && previousSynchronousIoSetting.HasValue)
                        {
                            bodyControlFeature.AllowSynchronousIO = previousSynchronousIoSetting.Value;
                        }
                    }
                }
                catch (JsonException)
                {
                    if (httpContext.Request.Body.CanSeek)
                    {
                        httpContext.Request.Body.Position = 0;
                    }

                    return null;
                }
                catch (IOException)
                {
                    if (httpContext.Request.Body.CanSeek)
                    {
                        httpContext.Request.Body.Position = 0;
                    }

                    return null;
                }
            }
        }
    }
}
