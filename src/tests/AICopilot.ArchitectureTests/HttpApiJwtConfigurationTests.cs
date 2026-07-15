using AICopilot.HttpApi;
using AICopilot.Services.Contracts.Authentication;
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
}
