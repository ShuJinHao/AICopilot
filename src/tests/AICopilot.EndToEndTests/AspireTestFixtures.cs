using AICopilot.AspireIntegrationTestKit;

namespace AICopilot.EndToEndTests;

public class AICopilotAppFixture : AICopilotAppEnvironment, IAsyncLifetime
{
}

public sealed class CoreAICopilotAppFixture : AICopilotAppFixture
{
    protected override bool EnableRagWorker => false;

    protected override bool EnableDataWorker => true;
}

public sealed class CloudSemanticSimulationAICopilotAppFixture : AICopilotAppFixture
{
    protected override void ConfigureAdditionalEnvironment()
    {
        ConfigureCloudReadonlySimulationEnvironment();
    }
}
