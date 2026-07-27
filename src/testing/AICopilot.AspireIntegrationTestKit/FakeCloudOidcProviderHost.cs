using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AICopilot.AspireIntegrationTestKit;

public sealed record FakeCloudOidcIdentity(
    string Subject,
    string PreferredUserName,
    string EmployeeNo,
    string EmployeeId,
    string DisplayName = "测试员工",
    string TenantId = "default",
    string DepartmentId = "D001",
    string DepartmentName = "制造一部",
    string StatusVersion = "v1",
    bool AccountEnabled = true,
    bool EmployeeActive = true);

public sealed class FakeCloudOidcProviderHost : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, PendingAuthorization> pendingCodes = new();
    private readonly ConcurrentDictionary<string, FakeCloudOidcIdentity> accessTokens = new();
    private readonly RSA rsa = RSA.Create(2048);
    private readonly RsaSecurityKey signingKey;
    private WebApplication? app;
    private FakeCloudOidcIdentity identity = new(
        "cloud-user-default",
        "E0001",
        "E0001",
        "employee-default");

    public FakeCloudOidcProviderHost()
    {
        signingKey = new RsaSecurityKey(rsa)
        {
            KeyId = $"fake-cloud-oidc-{Guid.NewGuid():N}"
        };
    }

    public Uri BaseUri { get; private set; } = null!;

    public string Issuer => BaseUri.GetLeftPart(UriPartial.Authority);

    public void SetIdentity(FakeCloudOidcIdentity value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Volatile.Write(ref identity, value);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (app is not null)
        {
            return;
        }

        var port = GetRandomUnusedPort();
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.ConfigureHttpJsonOptions(
            options => options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);

        var webApp = builder.Build();
        BaseUri = new Uri($"http://127.0.0.1:{port}");

        webApp.MapGet("/.well-known/openid-configuration", () => Results.Json(new
        {
            issuer = Issuer,
            authorization_endpoint = $"{Issuer}/authorize",
            token_endpoint = $"{Issuer}/token",
            userinfo_endpoint = $"{Issuer}/userinfo",
            jwks_uri = $"{Issuer}/jwks",
            response_types_supported = new[] { "code" },
            response_modes_supported = new[] { "query" },
            subject_types_supported = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { SecurityAlgorithms.RsaSha256 },
            token_endpoint_auth_methods_supported = new[] { "none" },
            scopes_supported = new[] { "openid", "profile" },
            claims_supported = SupportedClaimNames
        }, JsonOptions));
        webApp.MapGet("/jwks", () => Results.Json(CreateJwks(), JsonOptions));
        webApp.MapGet("/authorize", HandleAuthorize);
        webApp.MapPost("/token", HandleTokenAsync);
        webApp.MapGet("/userinfo", HandleUserInfo);

        await webApp.StartAsync(cancellationToken);
        app = webApp;
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }

        rsa.Dispose();
    }

    private IResult HandleAuthorize(HttpRequest request)
    {
        var redirectUri = request.Query["redirect_uri"].ToString();
        var state = request.Query["state"].ToString();
        var nonce = request.Query["nonce"].ToString();
        if (string.IsNullOrWhiteSpace(redirectUri) ||
            string.IsNullOrWhiteSpace(state) ||
            string.IsNullOrWhiteSpace(nonce))
        {
            return Results.BadRequest();
        }

        var code = Guid.NewGuid().ToString("N");
        pendingCodes[code] = new PendingAuthorization(
            Volatile.Read(ref identity),
            nonce);
        return Results.Redirect(AppendQuery(
            redirectUri,
            ("code", code),
            ("state", state)));
    }

    private async Task<IResult> HandleTokenAsync(HttpRequest request)
    {
        var form = await request.ReadFormAsync();
        var code = form["code"].ToString();
        var clientId = form["client_id"].ToString();
        if (!pendingCodes.TryRemove(code, out var pending) ||
            !string.Equals(clientId, "aicopilot", StringComparison.Ordinal))
        {
            return Results.Json(
                new { error = "invalid_grant" },
                JsonOptions,
                statusCode: StatusCodes.Status400BadRequest);
        }

        var accessToken = $"access-{Guid.NewGuid():N}";
        accessTokens[accessToken] = pending.Identity;
        return Results.Json(new
        {
            token_type = "Bearer",
            access_token = accessToken,
            expires_in = 300,
            id_token = CreateIdToken(pending)
        }, JsonOptions);
    }

    private IResult HandleUserInfo(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase) ||
            !accessTokens.TryGetValue(authorization[bearerPrefix.Length..], out var value))
        {
            return Results.Unauthorized();
        }

        return Results.Json(CreateClaimsPayload(value), JsonOptions);
    }

    private string CreateIdToken(PendingAuthorization pending)
    {
        var claims = CreateClaims(pending.Identity).Append(new Claim("nonce", pending.Nonce));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "aicopilot",
            Subject = new ClaimsIdentity(claims),
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow.AddSeconds(-5),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private object CreateJwks()
    {
        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        return new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    kid = signingKey.KeyId,
                    alg = SecurityAlgorithms.RsaSha256,
                    n = Base64UrlEncoder.Encode(parameters.Modulus),
                    e = Base64UrlEncoder.Encode(parameters.Exponent)
                }
            }
        };
    }

    private static object CreateClaimsPayload(FakeCloudOidcIdentity value)
    {
        return new
        {
            sub = value.Subject,
            preferred_username = value.PreferredUserName,
            name = value.DisplayName,
            tenant_id = value.TenantId,
            employee_id = value.EmployeeId,
            employee_no = value.EmployeeNo,
            account_enabled = value.AccountEnabled.ToString().ToLowerInvariant(),
            employee_active = value.EmployeeActive.ToString().ToLowerInvariant(),
            department_id = value.DepartmentId,
            department_name = value.DepartmentName,
            status_version = value.StatusVersion
        };
    }

    private static IEnumerable<Claim> CreateClaims(FakeCloudOidcIdentity value)
    {
        yield return new Claim("sub", value.Subject);
        yield return new Claim("preferred_username", value.PreferredUserName);
        yield return new Claim("name", value.DisplayName);
        yield return new Claim("tenant_id", value.TenantId);
        yield return new Claim("employee_id", value.EmployeeId);
        yield return new Claim("employee_no", value.EmployeeNo);
        yield return new Claim("account_enabled", value.AccountEnabled.ToString().ToLowerInvariant());
        yield return new Claim("employee_active", value.EmployeeActive.ToString().ToLowerInvariant());
        yield return new Claim("department_id", value.DepartmentId);
        yield return new Claim("department_name", value.DepartmentName);
        yield return new Claim("status_version", value.StatusVersion);
    }

    private static string AppendQuery(string uri, params (string Name, string Value)[] values)
    {
        var separator = uri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return uri + separator + string.Join(
            '&',
            values.Select(value =>
                $"{Uri.EscapeDataString(value.Name)}={Uri.EscapeDataString(value.Value)}"));
    }

    private static int GetRandomUnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static readonly string[] SupportedClaimNames =
    [
        "sub",
        "preferred_username",
        "name",
        "tenant_id",
        "employee_id",
        "employee_no",
        "account_enabled",
        "employee_active",
        "department_id",
        "department_name",
        "status_version"
    ];

    private sealed record PendingAuthorization(
        FakeCloudOidcIdentity Identity,
        string Nonce);
}
