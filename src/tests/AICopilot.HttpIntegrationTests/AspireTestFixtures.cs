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
