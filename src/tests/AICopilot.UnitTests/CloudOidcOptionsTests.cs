using AICopilot.Services.Contracts.Authentication;

namespace AICopilot.UnitTests;

public sealed class CloudOidcOptionsTests
{
    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://[::1]:8080")]
    public void EnsureValid_ShouldAllowDevelopmentLoopbackHttpIssuer(string issuer)
    {
        CreateOptions(issuer, requireHttpsMetadata: false).EnsureValid("Development");
    }

    [Theory]
    [InlineData("Production", "http://localhost:8080", false)]
    [InlineData("Production", "https://cloud.example.com", false)]
    [InlineData("Development", "http://cloud.example.com", false)]
    [InlineData("Development", "https://cloud.example.com", false)]
    public void EnsureValid_ShouldRejectInsecureClientMetadataConfiguration(
        string environmentName,
        string issuer,
        bool requireHttpsMetadata)
    {
        Action act = () => CreateOptions(issuer, requireHttpsMetadata).EnsureValid(environmentName);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValid_ShouldAllowProductionHttpsIssuerWithHttpsMetadata()
    {
        CreateOptions("https://cloud.example.com", requireHttpsMetadata: true).EnsureValid("Production");
    }

    [Theory]
    [InlineData("http://10.0.0.10:81")]
    [InlineData("http://192.168.1.10:81")]
    [InlineData("http://172.16.0.10:81")]
    [InlineData("http://172.31.255.10:81")]
    [InlineData("http://localhost:81")]
    [InlineData("http://cloud.internal.example:81")]
    [InlineData("http://cloud.factory.internal:81")]
    [InlineData("http://cloud.lan:81")]
    [InlineData("http://cloud.local:81")]
    public void EnsureValid_ShouldAllowExplicitIntranetHttpOidc(string issuer)
    {
        var options = CreateOptions(issuer, requireHttpsMetadata: true, allowIntranetHttpOidc: true);

        options.EnsureValid("Production");

        options.GetEffectiveRequireHttpsMetadata().Should().BeFalse();
        options.GetEffectiveExternalCookieName().Should().Be("AICopilot-CloudOidc-External");
        options.UseHttpCompatibleRemoteCookies().Should().BeTrue();
    }

    [Fact]
    public void EnsureValid_ShouldRejectPublicHttpEvenWhenIntranetHttpOidcIsEnabled()
    {
        var options = CreateOptions(
            "http://cloud.example.com",
            requireHttpsMetadata: true,
            allowIntranetHttpOidc: true);

        ((Action)(() => options.EnsureValid("Production"))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void HttpsMode_ShouldKeepHostCookiePrefix()
    {
        var options = CreateOptions("https://cloud.example.com", requireHttpsMetadata: true);

        options.GetEffectiveExternalCookieName().Should().Be("__Host-AICopilot-CloudOidc-External");
        options.UseHttpCompatibleRemoteCookies().Should().BeFalse();
    }

    private static CloudOidcOptions CreateOptions(
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
}
