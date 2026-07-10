using AICopilot.HttpApi;
using AICopilot.Infrastructure.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AICopilot.BackendTests;

public sealed class JwtSettingsTests
{
    [Fact]
    public void Defaults_ShouldKeepThirtyMinuteAccessTokenLifetime()
    {
        CreateValidSettings().AccessTokenExpirationMinutes.Should().Be(30);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    public void EnsureValid_ShouldRejectBlankIssuerAndAudience(string field)
    {
        var settings = CreateValidSettings();
        if (field == "issuer")
        {
            settings.Issuer = " ";
        }
        else
        {
            settings.Audience = " ";
        }

        Action act = settings.EnsureValid;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*JwtSettings:{(field == "issuer" ? "Issuer" : "Audience")}*");
    }

    [Fact]
    public void EnsureValid_ShouldRejectSixtyThreeCharacterSecretWithoutLeakingIt()
    {
        var secret = new string('s', JwtSettings.MinimumSecretKeyLength - 1);
        var settings = CreateValidSettings() with { SecretKey = secret };

        var exception = settings.Invoking(value => value.EnsureValid())
            .Should().Throw<InvalidOperationException>()
            .Which;

        exception.Message.Should().Contain("at least 64 characters");
        exception.Message.Should().NotContain(secret);
    }

    [Fact]
    public void EnsureValid_ShouldAcceptSixtyFourCharacterSecret()
    {
        var settings = CreateValidSettings() with
        {
            SecretKey = new string('s', JwtSettings.MinimumSecretKeyLength)
        };

        settings.Invoking(value => value.EnsureValid()).Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EnsureValid_ShouldRejectNonPositiveAccessTokenLifetime(int lifetimeMinutes)
    {
        var settings = CreateValidSettings() with
        {
            AccessTokenExpirationMinutes = lifetimeMinutes
        };

        settings.Invoking(value => value.EnsureValid())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*AccessTokenExpirationMinutes*");
    }

    [Fact]
    public void HttpApiOptionsConfiguration_ShouldFailFastForWeakSecretWithoutDeployScript()
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

    private static JwtSettings CreateValidSettings()
    {
        return new JwtSettings
        {
            Issuer = "AICopilot",
            Audience = "AICopilot.Web",
            SecretKey = new string('s', JwtSettings.MinimumSecretKeyLength)
        };
    }
}
