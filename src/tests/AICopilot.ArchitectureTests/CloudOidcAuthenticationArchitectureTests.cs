using AICopilot.HttpApi;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace AICopilot.ArchitectureTests;

public sealed class CloudOidcAuthenticationArchitectureTests
{
    [Fact]
    public void AuthenticationConfiguration_ShouldUseQueryResponseMode()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        HttpApiAuthenticationConfiguration.AddHttpApiAuthentication(
            services,
            new JwtSettings
            {
                Issuer = "AICopilot",
                Audience = "AICopilot",
                SecretKey = new string('x', JwtSettings.MinimumSecretKeyLength)
            },
            new CloudOidcOptions
            {
                Enabled = true,
                Issuer = "https://identity.example.test",
                ClientId = "aicopilot"
            },
            new CloudIdentityStatusOptions());

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(CloudOidcAuthenticationDefaults.AuthenticationScheme);

        options.ResponseMode.Should().Be(OpenIdConnectResponseMode.Query);
    }
}
