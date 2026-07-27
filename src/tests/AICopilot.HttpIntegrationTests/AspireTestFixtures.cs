using AICopilot.AspireIntegrationTestKit;

namespace AICopilot.HttpIntegrationTests;

public class AICopilotAppFixture : AICopilotAppEnvironment, IAsyncLifetime
{
}

public sealed class CoreAICopilotAppFixture : AICopilotAppFixture
{
    protected override bool EnableRagWorker => false;

    protected override bool EnableDataWorker => true;
}

public sealed class CloudOidcHttpAppFixture : AICopilotAppFixture
{
    private readonly FakeCloudOidcProviderHost provider = new();

    public FakeCloudOidcProviderHost Provider => provider;

    protected override bool EnableRagWorker => false;

    protected override bool EnableDataWorker => false;

    protected override Task StartAdditionalTestHostsAsync()
    {
        return provider.StartAsync();
    }

    protected override void ConfigureAdditionalEnvironment()
    {
        SetEnvironmentVariable("CloudOidc__Enabled", "true");
        SetEnvironmentVariable("CloudOidc__Issuer", provider.Issuer);
        SetEnvironmentVariable("CloudOidc__AllowIntranetHttpOidc", "true");
        SetEnvironmentVariable("CloudOidc__RequireHttpsMetadata", "false");
        SetEnvironmentVariable("CloudOidc__ClientId", "aicopilot");
    }

    protected override ValueTask DisposeAdditionalTestHostsAsync()
    {
        return provider.DisposeAsync();
    }
}
