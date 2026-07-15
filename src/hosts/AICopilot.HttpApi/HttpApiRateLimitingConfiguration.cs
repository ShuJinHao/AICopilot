using AICopilot.HttpApi.Infrastructure;
using AICopilot.Services.Contracts.Http;
using AICopilot.SharedKernel.Result;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace AICopilot.HttpApi;

internal static class HttpApiRateLimitingConfiguration
{
    public static void AddHttpApiRateLimiting(IServiceCollection services, IConfiguration configuration)
    {
        services.AddRateLimiter(options =>
        {
            var defaultTokenLimit = configuration.GetValue("RateLimiting:Default:TokenLimit", 60);
            var defaultTokensPerPeriod = configuration.GetValue("RateLimiting:Default:TokensPerPeriod", 60);
            var loginTokenLimit = configuration.GetValue("RateLimiting:Login:TokenLimit", 5);
            var loginTokensPerPeriod = configuration.GetValue("RateLimiting:Login:TokensPerPeriod", 5);
            var chatTokenLimit = configuration.GetValue("RateLimiting:Chat:TokenLimit", 12);
            var chatTokensPerPeriod = configuration.GetValue("RateLimiting:Chat:TokensPerPeriod", 12);
            var identityManagementTokenLimit = configuration.GetValue("RateLimiting:IdentityManagement:TokenLimit", 10);
            var identityManagementTokensPerPeriod = configuration.GetValue("RateLimiting:IdentityManagement:TokensPerPeriod", 10);

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
                            extensions),
                        traceIdentifier: context.HttpContext.TraceIdentifier),
                    cancellationToken);
            };
        });
    }

    private static string GetDefaultPolicyPartitionKey(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var clientKey = string.IsNullOrWhiteSpace(userId)
            ? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous"
            : userId;

        return $"{httpContext.Request.Method}:{httpContext.Request.Path}:{clientKey}";
    }

    private static string GetChatPolicyPartitionKey(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return $"chat:{(string.IsNullOrWhiteSpace(userId) ? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous" : userId)}";
    }

    private static string GetLoginPolicyPartitionKey(HttpContext httpContext)
    {
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        var username = TryReadLoginUsername(httpContext);
        var normalizedUsername = string.IsNullOrWhiteSpace(username)
            ? "anonymous"
            : username.Trim().ToUpperInvariant();

        return $"login:{normalizedUsername}:{ipAddress}";
    }

    private static string GetIdentityManagementPolicyPartitionKey(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var clientKey = string.IsNullOrWhiteSpace(userId)
            ? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous"
            : userId;

        return $"identity-management:{httpContext.Request.Method}:{httpContext.Request.Path}:{clientKey}";
    }

    private static string? TryReadLoginUsername(HttpContext httpContext)
    {
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
