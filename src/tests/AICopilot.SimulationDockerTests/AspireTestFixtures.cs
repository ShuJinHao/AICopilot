using AICopilot.AspireIntegrationTestKit;

namespace AICopilot.SimulationDockerTests;

public sealed class AgentSimulationAICopilotAppFixture : AICopilotAppEnvironment, IAsyncLifetime
{
    protected override bool EnableRagWorker => false;

    protected override bool EnableDataWorker => true;

    protected override void ConfigureAdditionalEnvironment()
    {
        ConfigureCloudReadonlySimulationEnvironment();
    }
}
