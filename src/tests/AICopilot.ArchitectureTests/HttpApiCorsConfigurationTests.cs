using AICopilot.HttpApi;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AICopilot.ArchitectureTests;

public sealed class HttpApiCorsConfigurationTests
{
    [Fact]
    public void DefaultAndExplicitOrigins_ShouldKeepSameOriginBoundary()
    {
        using var defaultProvider = BuildProvider(new Dictionary<string, string?>());
        var defaultPolicy = defaultProvider.GetRequiredService<IOptions<CorsOptions>>()
            .Value.GetPolicy(HttpApiCorsConfiguration.PolicyName);

        defaultPolicy.Should().NotBeNull();
        defaultPolicy!.IsOriginAllowed("https://untrusted.example").Should().BeFalse();
        defaultPolicy.Origins.Should().BeEmpty();

        using var explicitProvider = BuildProvider(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = " https://ai.example:443/ ",
            ["Cors:AllowedOrigins:1"] = "https://AI.example:443"
        });
        var explicitPolicy = explicitProvider.GetRequiredService<IOptions<CorsOptions>>()
            .Value.GetPolicy(HttpApiCorsConfiguration.PolicyName);

        explicitPolicy.Should().NotBeNull();
        explicitPolicy!.Origins.Should().Equal("https://ai.example");
        explicitPolicy.IsOriginAllowed("https://ai.example").Should().BeTrue();
        explicitPolicy.IsOriginAllowed("https://untrusted.example").Should().BeFalse();

        var wildcard = new HttpApiCorsOptions { AllowedOrigins = ["https://*.example"] };
        var action = wildcard.EnsureValid;
        action.Should().Throw<InvalidOperationException>().WithMessage("*wildcard origins are forbidden*");
    }

    private static ServiceProvider BuildProvider(IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        HttpApiCorsConfiguration.AddHttpApiCors(services, configuration);
        return services.BuildServiceProvider();
    }
}
