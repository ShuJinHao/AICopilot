namespace AICopilot.HttpApi;

internal sealed class HttpApiCorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; init; } = [];

    public string[] GetNormalizedAllowedOrigins()
    {
        return AllowedOrigins
            .Select(NormalizeOrigin)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void EnsureValid()
    {
        foreach (var origin in AllowedOrigins)
        {
            _ = NormalizeOrigin(origin);
        }
    }

    private static string NormalizeOrigin(string origin)
    {
        var trimmed = origin.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("Cors:AllowedOrigins must not contain empty values.");
        }

        if (trimmed.Contains('*', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cors:AllowedOrigins must use explicit origins; wildcard origins are forbidden.");
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Cors:AllowedOrigins values must be absolute http/https origins.");
        }

        if (uri.AbsolutePath != "/" ||
            !string.IsNullOrWhiteSpace(uri.Query) ||
            !string.IsNullOrWhiteSpace(uri.Fragment))
        {
            throw new InvalidOperationException("Cors:AllowedOrigins values must be origins only, without path, query, or fragment.");
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}

internal static class HttpApiCorsConfiguration
{
    public const string PolicyName = "AICopilotExplicitOrigins";

    public static void AddHttpApiCors(IServiceCollection services, IConfiguration configuration)
    {
        var corsOptions = configuration
            .GetSection(HttpApiCorsOptions.SectionName)
            .Get<HttpApiCorsOptions>() ?? new HttpApiCorsOptions();
        corsOptions.EnsureValid();

        var allowedOrigins = corsOptions.GetNormalizedAllowedOrigins();
        services.Configure<HttpApiCorsOptions>(configuration.GetSection(HttpApiCorsOptions.SectionName));
        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                if (allowedOrigins.Length > 0)
                {
                    policy.WithOrigins(allowedOrigins);
                }
                else
                {
                    policy.SetIsOriginAllowed(_ => false);
                }

                policy.AllowAnyHeader();
                policy.AllowAnyMethod();
            });
        });
    }
}
