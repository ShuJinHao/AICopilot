using System.Security.Claims;
using AICopilot.HttpApi.Controllers;
using AICopilot.HttpApi.Infrastructure;
using AICopilot.IdentityService.Commands;
using AICopilot.SharedKernel.Result;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

[Trait("Suite", "Identity")]
public sealed class CloudOidcLoginTestsControllerSecurity
{
    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldReturnAiJwtInBodyAndClearExternalCookie()
    {
        var authService = new RecordingAuthenticationService(
            AuthenticateResult.Success(new AuthenticationTicket(
                CreateCloudPrincipal(),
                CloudOidcAuthenticationDefaults.ExternalCookieScheme)));
        var sender = new StaticSender(Result.Success(new LoginUserDto("E0001", "ai-token")));
        var controller = CreateController(sender, authService);

        var result = await controller.FinalizeCloudOidcLogin();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new LoginUserDto("E0001", "ai-token"));
        controller.HttpContext.Response.Headers.Location.Should().BeEmpty();
        authService.SignedOutSchemes.Should().ContainSingle()
            .Which.Should().Be(CloudOidcAuthenticationDefaults.ExternalCookieScheme);
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldClearExternalCookie_WhenPrincipalIsInvalid()
    {
        var authService = new RecordingAuthenticationService(AuthenticateResult.NoResult());
        var controller = CreateController(new ThrowingSender(), authService);

        var result = await controller.FinalizeCloudOidcLogin();

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        controller.HttpContext.Response.Headers.Location.Should().BeEmpty();
        authService.SignedOutSchemes.Should().ContainSingle()
            .Which.Should().Be(CloudOidcAuthenticationDefaults.ExternalCookieScheme);
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldClearExternalCookie_WhenFinalizeFails()
    {
        var authService = new RecordingAuthenticationService(
            AuthenticateResult.Success(new AuthenticationTicket(
                CreateCloudPrincipal(),
                CloudOidcAuthenticationDefaults.ExternalCookieScheme)));
        var sender = new StaticSender(Result.Unauthorized(new ApiProblemDescriptor(
            AuthProblemCodes.ExternalIdentityConflict,
            "binding conflict")));
        var controller = CreateController(sender, authService);

        var result = await controller.FinalizeCloudOidcLogin();

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        controller.HttpContext.Response.Headers.Location.Should().BeEmpty();
        authService.SignedOutSchemes.Should().ContainSingle()
            .Which.Should().Be(CloudOidcAuthenticationDefaults.ExternalCookieScheme);
    }

    [Fact]
    public async Task FinalizeCloudOidcLogin_ShouldClearExternalCookie_WhenSenderThrows()
    {
        var authService = new RecordingAuthenticationService(
            AuthenticateResult.Success(new AuthenticationTicket(
                CreateCloudPrincipal(),
                CloudOidcAuthenticationDefaults.ExternalCookieScheme)));
        var controller = CreateController(new ThrowingSender(), authService);

        var act = () => controller.FinalizeCloudOidcLogin();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Sender should not be called by this test.");
        controller.HttpContext.Response.Headers.Location.Should().BeEmpty();
        authService.SignedOutSchemes.Should().ContainSingle()
            .Which.Should().Be(CloudOidcAuthenticationDefaults.ExternalCookieScheme);
    }

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://[::1]:8080")]
    public void CloudOidcOptions_ShouldAllowDevelopmentLoopbackHttpIssuer(string issuer)
    {
        var options = CreateCloudOidcOptions(issuer, requireHttpsMetadata: false);

        options.EnsureValid("Development");
    }

    [Theory]
    [InlineData("Production", "http://localhost:8080", false)]
    [InlineData("Production", "https://cloud.example.com", false)]
    [InlineData("Development", "http://cloud.example.com", false)]
    [InlineData("Development", "https://cloud.example.com", false)]
    public void CloudOidcOptions_ShouldRejectInsecureClientMetadataConfiguration(
        string environmentName,
        string issuer,
        bool requireHttpsMetadata)
    {
        var options = CreateCloudOidcOptions(issuer, requireHttpsMetadata);

        Action act = () => options.EnsureValid(environmentName);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CloudOidcOptions_ShouldAllowProductionHttpsIssuerWithHttpsMetadata()
    {
        var options = CreateCloudOidcOptions("https://cloud.example.com", requireHttpsMetadata: true);

        options.EnsureValid("Production");
    }

    [Theory]
    [InlineData("http://10.98.90.154:81")]
    [InlineData("http://192.168.1.10:81")]
    [InlineData("http://172.16.0.10:81")]
    [InlineData("http://172.31.255.10:81")]
    [InlineData("http://localhost:81")]
    public void CloudOidcOptions_ShouldAllowExplicitIntranetHttpOidc(string issuer)
    {
        var options = CreateCloudOidcOptions(
            issuer,
            requireHttpsMetadata: true,
            allowIntranetHttpOidc: true);

        options.EnsureValid("Production");

        options.GetEffectiveRequireHttpsMetadata().Should().BeFalse();
        options.GetEffectiveExternalCookieName().Should().Be("AICopilot-CloudOidc-External");
        options.UseHttpCompatibleRemoteCookies().Should().BeTrue();
    }

    [Fact]
    public void CloudOidcOptions_ShouldRejectPublicHttpEvenWhenIntranetHttpOidcIsEnabled()
    {
        var options = CreateCloudOidcOptions(
            "http://cloud.example.com",
            requireHttpsMetadata: true,
            allowIntranetHttpOidc: true);

        Action act = () => options.EnsureValid("Production");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CloudOidcOptions_ShouldKeepHostCookiePrefixWhenHttpsMode()
    {
        var options = CreateCloudOidcOptions("https://cloud.example.com", requireHttpsMetadata: true);

        options.GetEffectiveExternalCookieName().Should().Be("__Host-AICopilot-CloudOidc-External");
        options.UseHttpCompatibleRemoteCookies().Should().BeFalse();
    }

    private static IdentityController CreateController(
        ISender sender,
        RecordingAuthenticationService authService)
    {
        var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(authService)
            .BuildServiceProvider();
        var schemeProvider = new AuthenticationSchemeProvider(Options.Create(new AuthenticationOptions()));
        schemeProvider.AddScheme(new AuthenticationScheme(
            CloudOidcAuthenticationDefaults.AuthenticationScheme,
            CloudOidcAuthenticationDefaults.AuthenticationScheme,
            typeof(IAuthenticationHandler)));

        var controller = new IdentityController(
            sender,
            schemeProvider,
            Options.Create(new CloudOidcOptions
            {
                Enabled = true,
                Issuer = "https://cloud.example.com",
                ClientId = "aicopilot"
            }));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = services
            }
        };

        return controller;
    }

    private static CloudOidcOptions CreateCloudOidcOptions(
        string issuer,
        bool requireHttpsMetadata,
        bool allowIntranetHttpOidc = false)
    {
        return new CloudOidcOptions
        {
            Enabled = true,
            Issuer = issuer,
            AllowIntranetHttpOidc = allowIntranetHttpOidc,
            ClientId = "aicopilot",
            CallbackPath = "/api/identity/cloud-oidc/callback",
            FrontendCompletionPath = "/cloud-login/complete",
            RequireHttpsMetadata = requireHttpsMetadata
        };
    }

    private static ClaimsPrincipal CreateCloudPrincipal()
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "cloud-user-1"),
                new Claim("preferred_username", "E0001"),
                new Claim("name", "E0001"),
                new Claim("tenant_id", CloudOidcIdentityProfile.DefaultTenantId),
                new Claim("employee_id", "employee-1"),
                new Claim("employee_no", "E0001"),
                new Claim("account_enabled", "true"),
                new Claim("employee_active", "true"),
                new Claim("status_version", "v1")
            ],
            "oidc"));
    }

    private sealed class RecordingAuthenticationService(AuthenticateResult authenticateResult)
        : IAuthenticationService
    {
        public List<string> SignedOutSchemes { get; } = [];

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            return Task.FromResult(authenticateResult);
        }

        public Task ChallengeAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            throw new InvalidOperationException("Challenge should not be called by this test.");
        }

        public Task ForbidAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            throw new InvalidOperationException("Forbid should not be called by this test.");
        }

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties)
        {
            throw new InvalidOperationException("SignIn should not be called by this test.");
        }

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            SignedOutSchemes.Add(scheme ?? string.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class StaticSender(Result<LoginUserDto> result) : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            if (request is FinalizeCloudOidcLoginCommand)
            {
                return Task.FromResult((TResponse)(object)result);
            }

            throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new InvalidOperationException("Void requests should not be sent by this test.");
        }

        public Task<object?> Send(
            object request,
            CancellationToken cancellationToken = default)
        {
            if (request is FinalizeCloudOidcLoginCommand)
            {
                return Task.FromResult<object?>(result);
            }

            throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Streams should not be created by this test.");
        }

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Streams should not be created by this test.");
        }
    }

    private sealed class ThrowingSender : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Sender should not be called by this test.");
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new InvalidOperationException("Sender should not be called by this test.");
        }

        public Task<object?> Send(
            object request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Sender should not be called by this test.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Streams should not be created by this test.");
        }

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Streams should not be created by this test.");
        }
    }
}
