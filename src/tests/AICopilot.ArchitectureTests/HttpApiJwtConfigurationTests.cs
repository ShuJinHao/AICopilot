using AICopilot.HttpApi;
using AICopilot.Services.Contracts;
using AICopilot.Infrastructure.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AICopilot.ArchitectureTests;

public sealed class HttpApiJwtConfigurationTests
{
    [Fact]
    public void ConfigureAndValidate_ShouldFailFastForWeakSecretWithoutDeployScript()
    {
        var weakSecret = new string('w', JwtSettings.MinimumSecretKeyLength - 1);
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Issuer"] = "AICopilot",
            ["JwtSettings:Audience"] = "AICopilot.Web",
            ["JwtSettings:SecretKey"] = weakSecret,
            ["JwtSettings:AccessTokenExpirationMinutes"] = "30"
        });

        var exception = ((Action)(() => HttpApiOptionsConfiguration.ConfigureAndValidate(builder)))
            .Should().Throw<InvalidOperationException>()
            .Which;

        exception.Message.Should().Contain("JwtSettings:SecretKey");
        exception.Message.Should().NotContain(weakSecret);
    }

    [Fact]
    public void ConfigureAndValidate_ShouldAllowOidcDelegationProbeWhenTypedAiReadQueriesAreDisabled()
    {
        var builder = CreateOidcBuilder(aiReadBaseUrl: "http://localhost:8080");

        var action = () => HttpApiOptionsConfiguration.ConfigureAndValidate(builder);

        action.Should().NotThrow();
    }

    [Fact]
    public void ConfigureAndValidate_ShouldRejectOidcWithoutDelegationProbeEndpoint()
    {
        var builder = CreateOidcBuilder(aiReadBaseUrl: string.Empty);

        var exception = ((Action)(() => HttpApiOptionsConfiguration.ConfigureAndValidate(builder)))
            .Should().Throw<InvalidOperationException>()
            .Which;

        exception.Message.Should().Contain("CloudAiRead:BaseUrl");
    }

    private static HostApplicationBuilder CreateOidcBuilder(string aiReadBaseUrl)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Issuer"] = "AICopilot",
            ["JwtSettings:Audience"] = "AICopilot.Web",
            ["JwtSettings:SecretKey"] = new string('s', JwtSettings.MinimumSecretKeyLength),
            ["JwtSettings:AccessTokenExpirationMinutes"] = "30",
            ["CloudOidc:Enabled"] = "true",
            ["CloudOidc:Issuer"] = "http://localhost:8080",
            ["CloudOidc:ClientId"] = "aicopilot",
            ["CloudOidc:RequireHttpsMetadata"] = "false",
            ["CloudOidc:Scopes:0"] = "openid",
            ["CloudOidc:Scopes:1"] = "profile",
            ["CloudOidc:Scopes:2"] = CloudDelegationDefaults.Scope,
            ["CloudAiRead:Enabled"] = "false",
            ["CloudAiRead:BaseUrl"] = aiReadBaseUrl
        });
        return builder;
    }
}
